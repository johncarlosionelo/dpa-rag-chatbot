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

public sealed class LlmOptions
{
    public string BaseUrl { get; init; } = "https://integrate.api.nvidia.com/v1";

    public IReadOnlyList<string> Models { get; init; } = [];

    public int MaxTokens { get; init; } = 420;

    public int TimeoutSeconds { get; init; } = 45;

    public double Temperature { get; init; } = 0.1;
}

public sealed class LlmClient
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly string _key;

    public LlmClient(HttpClient http, LlmOptions options, string key)
    {
        _http = http;
        _options = options;
        _key = key;
    }

    public async Task<LlmReply> CompleteAsync(IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        Exception? last = null;

        for (var attempt = 0; attempt < _options.Models.Count; attempt++)
        {
            var model = _options.Models[attempt];
            try
            {
                return await SendAsync(model, turns, attempt > 0, ct);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
                Console.Error.WriteLine($"model {model} failed: {ex.Message}");
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
                Console.Error.WriteLine($"model {model} timed out");
            }
        }

        throw new InvalidOperationException("every configured model failed", last);
    }

    private async Task<LlmReply> SendAsync(string model, IReadOnlyList<ChatTurn> turns, bool fallback, CancellationToken ct)
    {
        var payload = new
        {
            model,
            messages = turns.Select(t => new { role = t.Role, content = t.Content }).ToArray(),
            max_tokens = _options.MaxTokens,
            temperature = _options.Temperature,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{model} returned {(int)response.StatusCode}: {Truncate(body, 180)}");
        }

        var parsed = JsonSerializer.Deserialize<CompletionResponse>(body);
        var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;

        return new LlmReply(text.Trim(), model, fallback);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed record CompletionResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<Choice>? Choices);

    private sealed record Choice(
        [property: JsonPropertyName("message")] Message? Message);

    private sealed record Message(
        [property: JsonPropertyName("content")] string? Content);
}
