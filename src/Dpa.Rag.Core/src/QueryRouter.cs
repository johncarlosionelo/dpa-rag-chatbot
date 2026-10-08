using System.Text.RegularExpressions;

namespace Dpa.Rag.Core;

public enum Intent
{
    Greeting,
    Meta,
    ExplicitSection,
    FollowUp,
    OutOfScope,
    General,
}

public sealed record Route(Intent Intent, string ResolvedQuestion, IReadOnlyList<string> SectionHints);

public sealed partial class QueryRouter
{
    private const string Act = "the Data Privacy Act of 2012";

    [GeneratedRegex(
        @"^\s*(hi|hey|hello|yo|sup|hiya|good\s(morning|afternoon|evening)|greetings|kamusta|kumusta|kumustahin)" +
        @"\b(\s+(there|team|all|ya|ako|po))?[\s!,.?]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Greeting();

    [GeneratedRegex(
        @"\b(what can you (do|help|answer)|who are you|what are you|how do you work|" +
        @"what is this|what do you know|what can i ask|are you (a )?(bot|ai|chatbot)|" +
        @"pwede mo ba\s+(?:ba\s+)?akong tulungan|tulungan mo ba ako|can you (help|assist) me|" +
        @"help me|what are you for|are (you|u)\s+(an?\s+|is\s+|a\s+)?(ai|bot|robot|chatbot|human)|" +
        @"ikaw ba (ba)? (ai|tao)|ai ka ba ba|ginagawa mo ba ako|galit ka ba|masyado ka (ba)?|" +
        @"sige( po)?|tulungan mo nalang ako|tulungan mo na lang ako|okay (lang|na)|" +
        @"nakakapag (tagalog|english)|kayong (tagalog|english)|what can you do|your purpose|" +
        @"who created you|who made you|who built you|who trained you|are you human)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Meta();

    [GeneratedRegex(
        @"(?:^|\s)(?:section|sec|article|art|seksyon|artikulo)\.?\s*(\d{1,3})(?:\b|$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitNumber();

    [GeneratedRegex(
        @"\b(expand|explain|elaborate|more|detail|details|deeper|go on|continue|buksan|lalong|" +
        @"ipaliwanag|paliwanag|why|how|really|simplify|easier|example|examples|summary|tl;?dr)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex BareFollowUp();

    [GeneratedRegex(
        @"\b(expand|explain|elaborate|go deeper|in detail|more detail|simpler|easier|" +
        @"in simple terms|like i am|what do you mean|can you clarify|clarify|buksan|lalong)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex FollowUp();


    [GeneratedRegex(@"\b(it|that|this|those|these|them|there)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Anaphora();

    [GeneratedRegex(@"\d{1,3}", RegexOptions.Compiled)]
    private static partial Regex BareNumber();

    [GeneratedRegex(
        @"(\d\s*[+\-x*/]\s*\d)|(\bplus\b|\bminus\b|\btimes\b|\bdivided\b|\bequals\b|\bsum\b|\bmultiply\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Arithmetic();

    private static readonly string[] Greetings =
    [
        "Hello. I have the Data Privacy Act of 2012, all 45 sections, loaded and searchable.",
        "Hi there. Ask me anything about RA 10379 and I will cite the section behind it.",
        "Hey there. The Act is loaded, all 45 sections. Ask about personal information, penalties, the Commission, or breach notification.",
    ];

    private static readonly string[] Scope =
    [
        $"I answer from the text of {Act}, section by section, with the section number for everything I say.",
        "Ask me about personal information, the data subject, the National Privacy Commission, security obligations, breach notification, retention, or penalties.",
        $"Everything I say comes from {Act} itself. If it is not in the Act, I will tell you plainly instead of guessing.",
    ];

    private readonly HashSet<string> _sections;

    public QueryRouter(IEnumerable<string> sections)
    {
        _sections = [.. sections.Select(s => s.Trim())];
    }

    private static readonly string[] GreetingsTagalog =
    [
        "Kumusta. Nasa system ko ang Data Privacy Act of 2012, lahat ng 45 seksyon, handa nang hanapin.",
        "Kumusta! Nasa system ko ang RA 10379, lahat ng 45 seksyon. Magtanong ka kahit ano, at ibibigay ko ang seksyon na pinagbatayan.",
        "Kumusta! Bukas ang batas. Itanong ang personal na impormasyon, ang data subject, ang Commission, ang seguridad, o ang parusa.",
    ];

    private static readonly string[] ScopeTagalog =
    [
        "Mula sa teksto mismo ng Data Privacy Act of 2012 ang lahat ng aking sagot, may bilang ng seksyon sa bawat isa.",
        "Itanong ang personal na impormasyon, ang data subject, ang National Privacy Commission, ang seguridad, ang abiso sa breach, ang pag-iimbak, o ang parusa.",
        "Kung wala sa batas ang tanong, sasabihin ko nang diretso sa halip na hula-hula.",
    ];

    private static readonly string[] RedirectTagalog =
    [
        "Hindi saklaw ng Data Privacy Act of 2012 ang tanong na iyon, kaya hindi ako maghuhula. Narito ang mga bagay na saklaw nito:",
        "Wala iyon sa batas. Narito ang mga pangunahing probisyon ng RA 10379:",
        "Nasa labas ng batas ang tanong na iyon. Kung gusto ninyong magtanong tungkol sa batas, narito ang mga simula:",
    ];

    private static readonly string[] NoMatchTagalog =
    [
        "Hindi ako nakahanap ng seksyon ng Data Privacy Act of 2012 na sumasagot sa tanong na iyon.",
        "Walang seksyon sa batas na tumutugon sa tanong na iyon.",
    ];

    public static bool IsFilipino(string question)
    {
        if (ForcesTagalog().IsMatch(question))
        {
            return true;
        }

        if (ForcesEnglish().IsMatch(question))
        {
            return false;
        }

        return Filipino().IsMatch(question);
    }

    [GeneratedRegex(
        @"\b(sa\s+tagalog|in\s+tagalog|sa\s+filipino|tagalog\s+(please|po)|pakisabi\s+sa\s+tagalog)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ForcesTagalog();

    [GeneratedRegex(
        @"\b(in\s+english|sanitize\s+tagalog|remove\s+tagalog)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ForcesEnglish();

    public string Greet(int turn, bool filipino = false)
    {
        var lines = filipino ? GreetingsTagalog : Greetings;
        return lines[Math.Abs(turn) % lines.Length];
    }

    public string DescribeScope(int turn, bool filipino = false)
    {
        var lines = filipino ? ScopeTagalog : Scope;
        return lines[Math.Abs(turn) % lines.Length];
    }

    public string RedirectText(string act, bool filipino)
    {
        var turn = Math.Abs(act.Length);
        var lines = filipino ? RedirectTagalog : new[]
        {
            "That one is outside this Act, so I will not guess. Here is what it does cover:",
        };

        return lines[turn % lines.Length];
    }

    public string NoMatchText(string act, bool filipino)
    {
        var lines = filipino ? NoMatchTagalog : new[]
        {
            "I could not find a section of the Act that answers that.",
        };

        return lines[Math.Abs(act.Length) % lines.Length];
    }

    public Route Route(string question, IReadOnlyList<string> historySections, int turn)
    {
        var trimmed = question.Trim();

        if (Greeting().IsMatch(trimmed))
        {
            return new Route(Intent.Greeting, trimmed, []);
        }

        if (Meta().IsMatch(trimmed))
        {
            return new Route(Intent.Meta, trimmed, []);
        }

        var explicitNumber = ExplicitNumber().Match(trimmed);

        if (explicitNumber.Success && _sections.Contains(explicitNumber.Groups[1].Value))
        {
            return new Route(Intent.ExplicitSection, trimmed, [explicitNumber.Groups[1].Value]);
        }

        var bare = BareNumber().Match(trimmed);

        if (bare.Success && _sections.Contains(bare.Value) && !Arithmetic().IsMatch(trimmed))
        {
            var withoutNumber = BareNumber().Replace(trimmed, " ").Trim();
            var bareOnly = withoutNumber.Length == 0 || withoutNumber.Length <= 2;

            if (bareOnly || FollowUp().IsMatch(trimmed))
            {
                return new Route(Intent.ExplicitSection, trimmed, [bare.Value]);
            }
        }

        var looksLikeFollowUp =
            FollowUp().IsMatch(trimmed) ||
            BareFollowUp().IsMatch(trimmed) ||
            (Anaphora().IsMatch(trimmed) && BareNumber().IsMatch(trimmed) && trimmed.Length < 60);

        if (looksLikeFollowUp && historySections.Count > 0)
        {
            return new Route(Intent.FollowUp, Rewrite(trimmed, historySections), historySections.ToList());
        }

        return new Route(Intent.General, trimmed, []);
    }

    private static readonly (string Needle, string Addition)[] Aliases =
    [
        ("signed", "approved sgd president of the philippines"),
        ("signatory", "approved sgd"),
        ("signatories", "approved sgd"),
        ("signature", "approved sgd"),
        ("signatures", "approved sgd"),
        ("who signed", "approved"),
        ("enacted", "approved effectivity"),
        ("when did it take effect", "effectivity take effect"),
        ("effectivity", "take effect approved"),
        ("most important", "declaration of policy scope"),
        ("main point", "declaration of policy"),
        ("biggest rule", "declaration of policy"),
    ];

    private static readonly (string Filipino, string English)[] FilipinoTerms =
    [
        ("multa", "penalty"),
        ("parusa", "penalty"),
        ("naparusahan", "penalty"),
        ("bakit", "why"),
        ("kailan", "when"),
        ("batas", "act law"),
        ("batasang", "act law"),
        ("datos", "data"),
        ("impormasyon", "information"),
        ("pribadong", "private"),
        ("pagproseso", "processing"),
        ("pinoproseso", "processing"),
        ("karapatan", "rights"),
        ("tao", "person individual"),
        ("may hawak", "controller"),
        ("kumpanya", "company corporation"),
        ("negosyo", "business"),
        ("utang", "obligation"),
        ("seksyon", "section"),
        ("artikulo", "article"),
        ("ligaw", "law"),
        ("sensitibo", "sensitive"),
        ("ligtas", "security safe"),
        ("pagsabog", "breach"),
        ("pananatili", "retention storage"),
    ];

    public static string Expand(string question)
    {
        var additions = new List<string>();

        foreach (var (needle, addition) in Aliases)
        {
            if (question.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                additions.Add(addition);
            }
        }

        foreach (var (filipino, english) in FilipinoTerms)
        {
            if (question.Contains(filipino, StringComparison.OrdinalIgnoreCase))
            {
                additions.Add(english);
            }
        }

        return additions.Count == 0 ? question : $"{question} {string.Join(' ', additions)}";
    }

    public bool IsOutOfScope(string question, double score) =>
        score < 0.12 && !ActTerms().IsMatch(question);

    private static string Rewrite(string question, IReadOnlyList<string> sections)
    {
        var focus = sections.Count > 0 ? $"section {sections[0]}" : "the previous answer";

        if (BareFollowUp().IsMatch(question))
        {
            return $"Expand and explain in more detail, in plain language, what {focus} requires under the Data Privacy Act of 2012.";
        }

        var rewritten = Anaphora().Replace(question, focus);

        return $"{rewritten} Focus on {focus} of the Data Privacy Act of 2012.";
    }

    [GeneratedRegex(
        @"\b(kamusta|kumusta|magandang|salamat|ako|ako'y|nang|po|ba|ano|bakit|kailan|paano|"
        + @"pano|saan|pwede|tulungan|kayo|ito|iyan|iyon|batas|multa|parusa|karapatan|datos|"
        + @"impormasyon|seksyon|artikulo|hindi|oo|sabihin|mayroon|wala|nais|gusto|"
        + @"pag|ngunit|probisyo|panahon|taon|oras)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Filipino();

    [GeneratedRegex(
        @"\b(summary|summarise|summarize|overview|outline|brief me|whole (act|law)|entire (act|law)|"
        + @"in general|generally|big picture|what is the (act|law)|buong (batas|aktblad)|buod)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Broad();

    public static bool IsBroad(string question) => Broad().IsMatch(question);

    [GeneratedRegex(
        @"\b(privacy|data|personal|information|act|law|batas|controller|processing|proseso|breach|" +
        @"penalt|multa|parusa|commission|datos|impormasyon|pribadong|karapatan|rights|obligation|utang|" +
        @"consent|retention|pananatili|security|ligtas|subject|tao|kumpanya|negosyo|seksyon|artikulo|\d{1,3})\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ActTerms();
}
