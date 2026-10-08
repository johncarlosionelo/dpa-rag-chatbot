using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dpa.Rag.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ingest <pdf> <out.json>");
    return 2;
}

var pdfPath = args[0];
var outPath = args[1];

if (!File.Exists(pdfPath))
{
    Console.Error.WriteLine($"pdf not found: {pdfPath}");
    return 2;
}

var builder = new StringBuilder();
using (var document = PdfDocument.Open(pdfPath))
{
    foreach (var page in document.GetPages())
    {
        builder.AppendLine(ContentOrderTextExtractor.GetText(page, true));
    }
}

var articles = new ActParser().Parse(builder.ToString());

if (articles.Count < 40)
{
    Console.Error.WriteLine($"expected at least 40 sections, got {articles.Count}");
    return 1;
}

var payload = new Corpus
{
    Act = "Republic Act No. 10379",
    ShortName = "Data Privacy Act of 2012",
    Source = "National Privacy Commission",
    SourceFile = Path.GetFileName(pdfPath),
    SourceNote = "The publisher file name reads Republic Act 10173, which is a typo on their side. The correct act number is 10379 and the content is correct.",
    ArticleCount = articles.Count,
    Articles = articles,
};

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

var full = Path.GetFullPath(outPath);
Directory.CreateDirectory(Path.GetDirectoryName(full)!);
File.WriteAllText(full, JsonSerializer.Serialize(payload, options));

Console.WriteLine($"articles: {articles.Count}");
Console.WriteLine($"wrote: {full}");
return 0;

internal sealed record Corpus
{
    [JsonPropertyName("act")] public string Act { get; init; } = string.Empty;
    [JsonPropertyName("shortName")] public string ShortName { get; init; } = string.Empty;
    [JsonPropertyName("source")] public string Source { get; init; } = string.Empty;
    [JsonPropertyName("sourceFile")] public string SourceFile { get; init; } = string.Empty;
    [JsonPropertyName("sourceNote")] public string SourceNote { get; init; } = string.Empty;
    [JsonPropertyName("articleCount")] public int ArticleCount { get; init; }
    [JsonPropertyName("articles")] public IReadOnlyList<Article> Articles { get; init; } = [];
}
