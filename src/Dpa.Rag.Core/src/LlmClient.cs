using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dpa.Rag.Core;

public sealed record ChatTurn(string Role, string Content);

public sealed record LlmReply(string Text, string Model, bool FromFallback)
{
    public IReadOnlyList<string> CitedSections { get; init; } = [];
}

public sealed record LlmTarget(string Name, string BaseUrl, string Model, string Key);

public sealed class LlmOptions
{
    public IReadOnlyList<LlmTarget> Targets { get; init; } = [];

    public int MaxTokens { get; init; } = 420;

    public int TimeoutSeconds { get; init; } = 45;

    public double Temperature { get; init; } = 0.1;
}

public sealed class LlmClient
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;

    public LlmClient(HttpClient http, LlmOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<LlmReply> CompleteAsync(IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        Exception? last = null;

        for (var attempt = 0; attempt < _options.Targets.Count; attempt++)
        {
            var target = _options.Targets[attempt];
            try
            {
                return await SendOnce(target, turns, attempt > 0, ct);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
                Console.Error.WriteLine($"{target.Name}/{target.Model} failed: {ex.Message}");
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
                Console.Error.WriteLine($"{target.Name}/{target.Model} timed out");
            }
        }

        throw new InvalidOperationException("every configured model failed", last);
    }

    private async Task<LlmReply> SendOnce(LlmTarget target, IReadOnlyList<ChatTurn> turns, bool fallback, CancellationToken ct)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            try
            {
                return await SendAsync(target, turns, fallback, ct);
            }
            catch (HttpRequestException ex) when (pass < 2 && !ct.IsCancellationRequested && (IsTransient(ex) || IsRateLimited(ex)))
            {
                await Task.Delay(IsRateLimited(ex) ? TimeSpan.FromSeconds(8) : TimeSpan.FromMilliseconds(600), ct);
            }
        }

        throw new HttpRequestException($"{target.Name} did not respond");
    }

    private static bool IsTransient(HttpRequestException ex) =>
        ex.InnerException is System.Net.Sockets.SocketException or TaskCanceledException;

    private static bool IsRateLimited(HttpRequestException ex) =>
        ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests;

    private async Task<LlmReply> SendAsync(LlmTarget target, IReadOnlyList<ChatTurn> turns, bool fallback, CancellationToken ct)
    {
        var payload = new
        {
            model = target.Model,
            messages = turns.Select(t => new { role = t.Role, content = t.Content }).ToArray(),
            max_tokens = _options.MaxTokens,
            temperature = _options.Temperature,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.BaseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Key);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{target.Model} returned {(int)response.StatusCode}: {Truncate(body, 180)}",
                null,
                response.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<CompletionResponse>(body);
        var message = parsed?.Choices?.FirstOrDefault()?.Message;
        var text = message?.Content ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            text = message?.Reasoning ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new HttpRequestException($"{target.Model} returned an empty completion");
        }

        return new LlmReply(text.Trim(), target.Model, fallback);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed record CompletionResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<Choice>? Choices);

    private sealed record Choice(
        [property: JsonPropertyName("message")] Message? Message);

    private sealed record Message(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("reasoning_content")] string? Reasoning);
}
