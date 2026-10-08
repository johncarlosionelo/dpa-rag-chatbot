using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dpa.Rag.Core;

public sealed record CorpusDocument(
    [property: JsonPropertyName("act")] string Act,
    [property: JsonPropertyName("shortName")] string ShortName,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("sourceFile")] string SourceFile,
    [property: JsonPropertyName("sourceNote")] string SourceNote,
    [property: JsonPropertyName("articleCount")] int ArticleCount,
    [property: JsonPropertyName("articles")] IReadOnlyList<Article> Articles);

public sealed record EmbeddedSection(string Number, string Title, string Text, float[] Vector);

public sealed class SectionIndexPayload
{
    public string Model { get; init; } = string.Empty;
    public int Dimensions { get; init; }
    public IReadOnlyList<EmbeddedSection> Sections { get; init; } = [];
}

public sealed class ActIndex : IDisposable
{
    private readonly List<EmbeddedSection> _sections;
    private readonly MiniLmEmbedder _embedder;
    private bool _disposed;

    public ActIndex(CorpusDocument corpus, MiniLmEmbedder embedder, IReadOnlyList<EmbeddedSection> sections)
    {
        Act = corpus.Act;
        ShortName = corpus.ShortName;
        _embedder = embedder;
        _sections = [.. sections];
        Terms.Rank(_sections);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _embedder.Dispose();
            _disposed = true;
        }
    }

    public string Act { get; }

    public string ShortName { get; }

    public int Count => _sections.Count;

    public IReadOnlyList<Scored> Search(string question, int take)
    {
        var query = _embedder.Embed(question);
        var terms = Terms.Of(question);
        var asksDefinition = Definitional.IsMatch(question);

        return _sections
            .Select(s =>
            {
                var dense = Cosine.Similarity(query, s.Vector);
                var exact = Terms.Weighted(terms, s.Title, s.Text);
                var blended = Blended(dense, exact);

                if (asksDefinition && IsDefinitions(s))
                {
                    blended += DefinitionalBoost;
                }

                return new Scored(s, blended, dense, exact);
            })
            .OrderByDescending(s => s.Score)
            .Take(take)
            .ToList();
    }

    private const double LexicalWeight = 0.55;
    private const double DefinitionalBoost = 0.30;

    private static readonly System.Text.RegularExpressions.Regex Definitional =
        new(@"\b(what is|what are|definition|defined as|meaning of)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex DefinitionsHeading =
        new(@"definition of terms",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsDefinitions(EmbeddedSection section) =>
        DefinitionsHeading.IsMatch(section.Title) || DefinitionsHeading.IsMatch(section.Text);

    private static double Blended(double dense, double lexical) =>
        ((1.0 - LexicalWeight) * dense) + (LexicalWeight * lexical);
}

public sealed record Scored(EmbeddedSection Section, double Score, double Dense, double Lexical)
{
    public Article ToArticle() => new(Section.Number, Section.Title, Section.Text);
}

public sealed class Terms
{
    private static readonly Regex Word = new(@"[a-z]{3,}", RegexOptions.Compiled);

    public const double TitleWeight = 0.6;

    private static readonly HashSet<string> Stop =
    [
        "what", "which", "when", "where", "does", "the", "and", "for", "may", "can",
        "must", "shall", "who", "how", "why", "are", "was", "were", "been", "has",
        "have", "had", "its", "their", "there", "them", "they", "this", "that", "with",
        "from", "into", "under", "about", "any", "all", "own", "out", "not", "his",
    ];

    public static HashSet<string> Of(string text) =>
        [.. Word.Matches(text.ToLowerInvariant()).Select(m => m.Value)];

    public static HashSet<string> Content(string text) =>
        [.. Of(text).Where(term => !Stop.Contains(term))];

    public static double Overlap(IReadOnlySet<string> query, IReadOnlySet<string> section) =>
        query.Count == 0 ? 0 : query.Count(term => section.Contains(term)) / (double)query.Count;

    public static double Weighted(IReadOnlySet<string> query, string title, string body) =>
        (TitleWeight * Idf(query, Of(title))) + ((1.0 - TitleWeight) * Idf(query, Of(body)));

    private static Func<IReadOnlySet<string>, IReadOnlySet<string>, double>? _rarity;

    public static void Rank(IReadOnlyList<EmbeddedSection> sections)
    {
        var frequency = new Dictionary<string, int>();
        foreach (var section in sections)
        {
            foreach (var term in Of(section.Title + " " + section.Text))
            {
                frequency[term] = frequency.GetValueOrDefault(term) + 1;
            }
        }

        _rarity = (query, field) =>
        {
            double total = 0;
            double hit = 0;
            foreach (var term in query)
            {
                var weight = Math.Log(1 + (sections.Count / (1.0 + frequency.GetValueOrDefault(term))));
                total += weight;
                if (field.Contains(term))
                {
                    hit += weight;
                }
            }

            return total == 0 ? 0 : hit / total;
        };
    }

    private static double Idf(IReadOnlySet<string> query, IReadOnlySet<string> field) =>
        _rarity is null ? Overlap(query, field) : _rarity(query, field);
}

public static class CorpusStore
{
    public static CorpusDocument Load(string path) =>
        JsonSerializer.Deserialize<CorpusDocument>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"corpus unreadable: {path}");

    public static ActIndex? LoadIndex(string path, CorpusDocument corpus, MiniLmEmbedder embedder)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var index = JsonSerializer.Deserialize<SectionIndexPayload>(File.ReadAllText(path));
        return index is null ? null : new ActIndex(corpus, embedder, index.Sections);
    }

    public static void SaveIndex(string path, string model, IReadOnlyList<EmbeddedSection> sections)
    {
        var payload = new SectionIndexPayload
        {
            Model = model,
            Dimensions = sections.Count > 0 ? sections[0].Vector.Length : 0,
            Sections = sections,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(payload));
    }
}

public sealed record BuildOptions(string ModelPath, string CorpusPath, string IndexPath);

public static class IndexBuilder
{
    public static ActIndex Build(BuildOptions options, out int created)
    {
        var corpus = CorpusStore.Load(options.CorpusPath);
        var embedder = new MiniLmEmbedder(options.ModelPath);

        var cached = CorpusStore.LoadIndex(options.IndexPath, corpus, embedder);
        if (cached is not null)
        {
            created = 0;
            return cached;
        }

        var sections = new List<EmbeddedSection>(corpus.Articles.Count);
        foreach (var article in corpus.Articles)
        {
            var text = TextShape.Flatten(article.Title, article.Text);
            sections.Add(new EmbeddedSection(article.Number, article.Title, article.Text, embedder.Embed(text)));
        }

        CorpusStore.SaveIndex(options.IndexPath, Path.GetFileName(options.ModelPath), sections);
        created = sections.Count;
        return new ActIndex(corpus, embedder, sections);
    }
}
