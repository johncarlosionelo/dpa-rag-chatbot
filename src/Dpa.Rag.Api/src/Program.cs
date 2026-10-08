using Dpa.Rag.Core;
using Microsoft.AspNetCore.Mvc;

var root = Root();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Path.Combine(root, "src/Dpa.Rag.Api"),
    WebRootPath = Path.Combine(root, "src/Dpa.Rag.Api/wwwroot"),
});

var indexOptions = builder.Configuration.GetSection("ActIndex").Get<BuildOptions>()
    ?? throw new InvalidOperationException("ActIndex configuration missing");

indexOptions = new BuildOptions(
    Rooted(indexOptions.ModelPath),
    Rooted(indexOptions.CorpusPath),
    Rooted(indexOptions.IndexPath));

var vectorOptions = builder.Configuration.GetSection("VectorStore").Get<VectorStoreOptions>()
    ?? throw new InvalidOperationException("VectorStore configuration missing");

vectorOptions = new VectorStoreOptions
{
    Host = vectorOptions.Host,
    Port = vectorOptions.Port,
    Collection = vectorOptions.Collection,
    ApiKey = ResolveOptional(vectorOptions.KeyEnv),
};

var llmSection = builder.Configuration.GetSection("Llm");
var baseOptions = llmSection.Get<LlmSettings>()
    ?? throw new InvalidOperationException("Llm configuration missing");

var pinned = (Environment.GetEnvironmentVariable("LLM_CHAIN") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

var providers = pinned.Count == 0
    ? baseOptions.Providers
    : [.. baseOptions.Providers.Where(provider => pinned.Contains(provider.Name))];

if (providers.Count == 0)
{
    throw new InvalidOperationException($"LLM_CHAIN matched no provider: {string.Join(",", pinned)}");
}

var targets = new List<LlmTarget>();
foreach (var provider in providers)
{
    var key = provider.KeyOptional
        ? Environment.GetEnvironmentVariable(provider.KeyEnv) ?? Placeholder(provider.Name)
        : ResolveKey(provider.KeyEnv);
    foreach (var model in provider.Models)
    {
        targets.Add(new LlmTarget(provider.Name, provider.BaseUrl.TrimEnd('/'), model, key));
    }
}

if (targets.Count == 0)
{  
    throw new InvalidOperationException("Llm:Providers must list at least one model");  
}  

var llmOptions = new LlmOptions
{
    Targets = targets,
    MaxTokens = baseOptions.MaxTokens,
    TimeoutSeconds = baseOptions.TimeoutSeconds,
    Temperature = baseOptions.Temperature,
};

Console.WriteLine(
    pinned.Count == 0
        ? $"llm chain: {string.Join(" then ", targets.Select(t => $"{t.Name}/{t.Model}"))}"
        : $"llm chain pinned to {string.Join(",", pinned)}: {string.Join(" then ", targets.Select(t => $"{t.Name}/{t.Model}"))}");

builder.Services.AddSingleton(llmOptions);
builder.Services.AddSingleton(indexOptions);
builder.Services.AddHttpClient(nameof(LlmClient), client =>
{
    client.Timeout = TimeSpan.FromSeconds(llmOptions.TimeoutSeconds);
    client.DefaultRequestHeaders.ConnectionClose = false;
});
builder.Services.AddSingleton<IVectorStore>(_ => new QdrantVectorStore(vectorOptions));
builder.Services.AddSingleton(sp =>
    ActIndexHolder.Open(indexOptions, sp.GetRequiredService<IVectorStore>()));
builder.Services.AddSingleton<ActAnswerer>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>();
    return new ActAnswerer(
        sp.GetRequiredService<ActIndexHolder>().Index,
        new LlmClient(http.CreateClient(nameof(LlmClient)), llmOptions));
});

var app = builder.Build();

var holder = app.Services.GetRequiredService<ActIndexHolder>();
await holder.SeedAsync(CancellationToken.None);
Console.WriteLine($"qdrant seeded: {holder.Index.Count} points");

app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/chat", async ([FromBody] ChatRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "question required" });
    }

    var ct = context.RequestAborted;
    Answer answer;
    try
    {
        answer = await app.Services
            .GetRequiredService<ActAnswerer>()
            .AskAsync(request.Question.Trim(), request.History ?? [], ct);
    }
    catch (Exception) when (!ct.IsCancellationRequested)
    {
        return Results.Json(
            new
            {
                answer = "The answer service did not respond in time. Please try that question again.",
                caveat = (string?)null,
                sources = Array.Empty<SourceRef>(),
                suggestions = Array.Empty<SourceRef>(),
                model = (string?)null,
                score = 0d,
                kind = "unavailable",
                scored = false,
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new ChatResponse(
        answer.Text,
        answer.Caveat,
        answer.Sources.Select(s => new SourceRef(
            s.Number,
            s.Title,
            s.Text)).ToList(),
        answer.Model,
        Math.Round(answer.Score, 4),
        answer.Kind,
        answer.Sources.Count > 0,
        answer.Suggestions.Select(s => new SourceRef(s.Number, s.Title, s.Text)).ToList()));
});

app.MapGet("/api/act", ([FromServices] ActIndexHolder holder) => Results.Ok(new
{
    act = holder.Index.Act,
    sections = holder.Index.Count,
    backend = holder.Index.Backend,
}));

app.MapFallbackToFile("index.html");

app.Run();

static string Rooted(string path) => Path.IsPathRooted(path) ? path : Path.Combine(Root(), path);

static string Root()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dpa.Rag.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? Directory.GetCurrentDirectory();
}

static string ResolveOptional(string keyEnv)
{
    foreach (var name in keyEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return string.Empty;
}

static string Placeholder(string name) => $"local-{name}";

static string ResolveKey(string keyEnv)
{
    foreach (var name in keyEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    throw new InvalidOperationException($"set one of: {keyEnv}");
}

internal sealed class LlmSettings
{
    public int MaxTokens { get; init; } = 420;

    public int TimeoutSeconds { get; init; } = 45;

    public double Temperature { get; init; } = 0.1;

    public IReadOnlyList<LlmProviderSettings> Providers { get; init; } = [];
}

internal sealed class LlmProviderSettings
{
    public string Name { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public string KeyEnv { get; init; } = string.Empty;

    public bool KeyOptional { get; init; }

    public IReadOnlyList<string> Models { get; init; } = [];
}

internal sealed class ActIndexHolder : IDisposable
{
    private ActIndexHolder(ActIndex index) => Index = index;

    public ActIndex Index { get; }

    public static ActIndexHolder Open(BuildOptions options, IVectorStore vectors)
    {
        var index = IndexBuilder.Build(options, vectors, out var embedded);

        if (embedded > 0)
        {
            Console.WriteLine($"embedded {embedded} sections into {options.IndexPath}");
        }
        else
        {
            Console.WriteLine($"loaded {index.Count} sections from {options.IndexPath}");
        }

        Console.WriteLine($"vector store: {index.Backend}");

        return new ActIndexHolder(index);
    }

    public async Task SeedAsync(CancellationToken ct) =>
        await Index.PersistAsync(Index.All, ct);

    public void Dispose() => Index.Dispose();
}

internal sealed record ChatRequest(string Question, ChatTurn[]? History);

internal sealed record SourceRef(string Number, string Title, string Text);

internal sealed record ChatResponse(
    string Answer,
    string? Caveat,
    IReadOnlyList<SourceRef> Sources,
    string? Model,
    double Score,
    string Kind,
    bool Scored,
    IReadOnlyList<SourceRef> Suggestions);
