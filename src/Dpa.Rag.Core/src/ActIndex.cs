using System.Text.RegularExpressions;
using System.Text;
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

public sealed partial class ActIndex : IDisposable
{
    private readonly Dictionary<string, EmbeddedSection> _byNumber;
    private readonly List<EmbeddedSection> _definitions;
    private readonly MiniLmEmbedder _embedder;
    private readonly IVectorStore _vectors;
    private bool _disposed;

    public ActIndex(
        CorpusDocument corpus,
        MiniLmEmbedder embedder,
        IVectorStore vectors,
        IReadOnlyList<EmbeddedSection> sections)
    {
        Act = corpus.Act;
        ShortName = corpus.ShortName;
        _embedder = embedder;
        _vectors = vectors;
        _byNumber = sections.ToDictionary(s => s.Number, StringComparer.Ordinal);
        _definitions = [.. _byNumber.Values.Where(IsDefinitions)];
        Terms.Rank([.. _byNumber.Values]);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _embedder.Dispose();
            _vectors.Dispose();
            _disposed = true;
        }
    }

    public string Backend => _vectors.Backend;

    public IReadOnlyCollection<string> SectionNumbers => _byNumber.Keys;

    public IReadOnlyList<EmbeddedSection> All => [.. _byNumber.Values];

    public EmbeddedSection? ByNumber(string number) =>
        _byNumber.TryGetValue(number, out var section) ? section : null;

    public string Act { get; }

    public string ShortName { get; }

    public int Count => _byNumber.Count;

    public async Task<IReadOnlyList<Scored>> SearchAsync(string question, int take, CancellationToken ct)
    {
        var query = _embedder.Embed(question);
        var terms = Terms.Of(question);
        var asksDefinition = Definitional.IsMatch(question) && ActTerms().IsMatch(question);

        var found = await _vectors.SearchAsync(query, Math.Min(take * 2, 32), ct);
        var scored = new List<Scored>(found.Count);

        foreach (var hit in found)
        {
            if (!_byNumber.TryGetValue(hit.Number, out var section))
            {
                continue;
            }

            var exact = Terms.Weighted(terms, section.Title, section.Text);
            var blended = Blended(hit.Score, exact);

            if (asksDefinition && IsDefinitions(section))
            {
                blended += DefinitionalBoost;
            }

            scored.Add(new Scored(section, blended, hit.Score, exact));
        }

        var ranked = scored.OrderByDescending(s => s.Score).Take(take).ToList();

        if (asksDefinition)
        {
            var definitions = _definitions;

            if (definitions.Count > 0)
            {
                var pinned = definitions
                    .Select(d => new Scored(d, DefinitionalBoost, 0.0, 0.0) { Pinned = true })
                    .ToList();

                pinned.AddRange(ranked.Where(r => definitions.All(d => d.Number != r.Section.Number)));
                ranked = pinned.Take(Math.Max(take, pinned.Count)).ToList();
            }
        }

        return ranked;
    }

    public async Task<IReadOnlyList<Scored>> SkeletonAsync(int take, CancellationToken ct)
    {
        var chapters = new List<EmbeddedSection>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var section in _byNumber.Values.OrderBy(s => int.TryParse(s.Number, out var n) ? n : int.MaxValue))
        {
            var chapter = ChapterOf(section.Title);

            if (chapter.Length > 0 && seen.Add(chapter))
            {
                chapters.Add(section);
            }

            if (chapters.Count >= take)
            {
                break;
            }
        }

        return [.. chapters.Select(section => new Scored(section, 1.0, 0.0, 0.0) { Pinned = true })];
    }

    private static string ChapterOf(string title)
    {
        var roman = title.AsSpan(0, title.IndexOf(' '));

        return roman.Length is > 0 && roman.Length <= 3 && roman.ContainsAny("IVXLCDM") && !char.IsDigit(roman[0])
            ? roman.ToString()
            : title;
    }

    public async Task PersistAsync(IReadOnlyList<EmbeddedSection> sections, CancellationToken ct)
    {
        await _vectors.EnsureCollectionAsync(sections.Count > 0 ? sections[0].Vector.Length : 384, ct);
        await _vectors.UpsertAsync(sections, ct);
    }

    private const double LexicalWeight = 0.55;
    private const double DefinitionalBoost = 1.20;

    private static readonly System.Text.RegularExpressions.Regex Definitional =
        new(@"\b(what is|what are|definition of|defined as)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex DefinitionsHeading =
        new(@"\bdefinition of terms\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

    [GeneratedRegex(
        @"\b(privacy|data|personal|information|act|law|batas|controller|processing|proseso|breach|" +
        @"penalt|multa|parusa|commission|datos|impormasyon|pribadong|karapatan|rights|obligation|utang|" +
        @"consent|retention|pananatili|security|ligtas|subject|tao|kumpanya|negosyo|seksyon|artikulo)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ActTerms();

    private static bool IsDefinitions(EmbeddedSection section) =>
        DefinitionsHeading.IsMatch(section.Title) || DefinitionsHeading.IsMatch(section.Text);

    private static double Blended(double dense, double lexical) =>
        ((1.0 - LexicalWeight) * dense) + (LexicalWeight * lexical);
}

public sealed record Scored(EmbeddedSection Section, double Score, double Dense, double Lexical)
{
    public bool Pinned { get; init; }

    public Article ToArticle() => new(Section.Number, Section.Title, Section.Text);
}

public sealed class Terms
{
    private static readonly Regex Word = new(@"[a-z]{3,}|\d{1,3}", RegexOptions.Compiled);

    public const double TitleWeight = 0.6;

    private static readonly HashSet<string> Stop =
    [
        "what", "which", "when", "where", "does", "the", "and", "for", "may", "can",
        "must", "shall", "who", "how", "why", "are", "was", "were", "been", "has",
        "have", "had", "its", "their", "there", "them", "they", "this", "that", "with",
        "from", "into", "under", "about", "any", "all", "own", "out", "not", "his",
        "sec", "section", "sections", "article", "act", "say", "said", "tell",
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
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static CorpusDocument Load(string path) =>
        JsonSerializer.Deserialize<CorpusDocument>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"corpus unreadable: {path}");

    public static ActIndex? LoadIndex(string path, CorpusDocument corpus, MiniLmEmbedder embedder, IVectorStore vectors)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var index = JsonSerializer.Deserialize<SectionIndexPayload>(File.ReadAllText(path), Options);
        return index is null ? null : new ActIndex(corpus, embedder, vectors, index.Sections);
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
    public static ActIndex Build(BuildOptions options, IVectorStore vectors, out int created)
    {
        var corpus = CorpusStore.Load(options.CorpusPath);
        var embedder = new MiniLmEmbedder(options.ModelPath);

        var cached = CorpusStore.LoadIndex(options.IndexPath, corpus, embedder, vectors);
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
        return new ActIndex(corpus, embedder, vectors, sections);
    }
}
