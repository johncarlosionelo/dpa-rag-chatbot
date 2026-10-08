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

var llmOptions = builder.Configuration.GetSection("Llm").Get<LlmOptions>()
    ?? throw new InvalidOperationException("Llm configuration missing");

if (llmOptions.Models.Count == 0)
{
    throw new InvalidOperationException("Llm:Models must list at least one model");
}

var llmKey = ResolveKey(builder.Configuration);
Console.WriteLine($"llm chain: {string.Join(" then ", llmOptions.Models)}");

builder.Services.AddSingleton(llmOptions);
builder.Services.AddSingleton(indexOptions);
builder.Services.AddHttpClient(nameof(LlmClient), client => client.Timeout = TimeSpan.FromSeconds(llmOptions.TimeoutSeconds));
builder.Services.AddSingleton(_ => ActIndexHolder.Open(indexOptions));
builder.Services.AddSingleton<ActAnswerer>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>();
    return new ActAnswerer(
        sp.GetRequiredService<ActIndexHolder>().Index,
        new LlmClient(http.CreateClient(nameof(LlmClient)), llmOptions, llmKey));
});

var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/chat", async ([FromBody] ChatRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "question required" });
    }

    var ct = context.RequestAborted;
    var answer = await app.Services.GetRequiredService<ActAnswerer>().AskAsync(request.Question.Trim(), ct);

    return Results.Ok(new ChatResponse(
        answer.Text,
        answer.Caveat,
        answer.Sources.Select(s => new SourceRef(
            s.Number,
            s.Title,
            s.Text.Length > 420 ? s.Text[..420] + "..." : s.Text)).ToList(),
        answer.Model,
        Math.Round(answer.Score, 4)));
});

app.MapGet("/api/act", ([FromServices] ActIndexHolder holder) => Results.Ok(new
{
    act = holder.Index.Act,
    sections = holder.Index.Count,
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

static string ResolveKey(IConfiguration configuration)
{
    var configured = configuration["Keys:LlmApiKey"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    foreach (var name in new[] { "DPA_RAG_LLM_KEY", "NVIDIA_API_KEY" })
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    throw new InvalidOperationException("set DPA_RAG_LLM_KEY or NVIDIA_API_KEY");
}

internal sealed class ActIndexHolder : IDisposable
{
    private ActIndexHolder(ActIndex index) => Index = index;

    public ActIndex Index { get; }

    public static ActIndexHolder Open(BuildOptions options)
    {
        var index = IndexBuilder.Build(options, out var created);
        if (created > 0)
        {
            Console.WriteLine($"embedded {created} sections into {options.IndexPath}");
        }
        else
        {
            Console.WriteLine($"loaded {index.Count} sections from {options.IndexPath}");
        }

        return new ActIndexHolder(index);
    }

    public void Dispose() => Index.Dispose();
}

internal sealed record ChatRequest(string Question);

internal sealed record SourceRef(string Number, string Title, string Text);

internal sealed record ChatResponse(
    string Answer,
    string? Caveat,
    IReadOnlyList<SourceRef> Sources,
    string? Model,
    double Score);
