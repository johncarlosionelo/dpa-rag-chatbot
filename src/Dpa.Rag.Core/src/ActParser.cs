using System.Text.RegularExpressions;

namespace Dpa.Rag.Core;

public sealed partial class ActParser
{
    [GeneratedRegex(@"^SEC(?:TION)?\.?\s+(\d+)\s*\.")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^CHAPTER\s+[IVXLC]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterLine();

    [GeneratedRegex(@"^\s*[A-Z][A-Z\s,&'()\-]{4,}\s*$")]
    private static partial Regex RunningHead();

    private static bool IsRunningHead(string value)
    {
        if (!RunningHead().IsMatch(value) || ChapterLine().IsMatch(value))
        {
            return false;
        }

        var letters = value.Count(char.IsLetter);
        return letters > 4 && !value.Contains(' ');
    }

    [GeneratedRegex(@"^(?:Back To Top|Home|About|DPA|Data|PICs|News|NPC|Issuances|Resources|Careers|Contact|Ask)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Chrome();

    [GeneratedRegex(@"\x0c|[\u00ad\u00a0\u2018\u2019]")]
    private static partial Regex Invisible();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex HorizontalSpace();

    [GeneratedRegex(@"\s+")]
    private static partial Regex AnySpace();

    private static readonly char[] DashChars = ['\u2013', '\u2014', '\u2015'];

    private static readonly string[] Separators = [" \u2013 ", " \u2014 ", " - "];

    private static readonly string[] Bleed =
    [
        " CHAPTER ", " DATA PRIVACY ACT OF ",
        " ABOUT GOVPH ", " NPC PRIVACY NOTICE ", " CONTACT US ", " BACK TO TOP ",
    ];

    private const string Enacted = "Be it enacted";
    private const int MinimumBodyCharacters = 15;

    public IReadOnlyList<Article> Parse(string rawText)
    {
        var lines = Split(rawText);
        var start = BodyStart(lines);
        if (start < 0)
        {
            return [];
        }

        var marks = new List<Mark>();
        for (var i = start; i < lines.Count; i++)
        {
            var match = Heading().Match(lines[i]);
            if (match.Success)
            {
                var split = Separate(lines[i][match.Length..]);
                marks.Add(new Mark(i, match.Groups[1].Value, split.Title, split.Body));
            }
        }

        var chapters = Chapters(lines, start);
        var articles = new List<Article>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < marks.Count; i++)
        {
            var mark = marks[i];
            if (!seen.Add(mark.Number))
            {
                continue;
            }

            var stop = i + 1 < marks.Count ? Trim(lines, mark.Line + 1, marks[i + 1].Line) : lines.Count;
            var slice = lines[(mark.Line + 1)..stop].Where(l => !ChapterLine().IsMatch(l));
            var continued = Clean(string.Join(' ', slice));

            var body = mark.Body.Length > 0 && continued.Length > 0
                ? $"{mark.Body} {continued}"
                : continued.Length > 0 ? continued : mark.Body;

            if (body.Length < MinimumBodyCharacters)
            {
                continue;
            }

            articles.Add(new Article(
                mark.Number,
                Compose(chapters.GetValueOrDefault(mark.Line) ?? string.Empty, mark.Title),
                body));
        }

        return articles;
    }

    private static int BodyStart(IReadOnlyList<string> lines)
    {
        var enacted = IndexOfLine(lines, l => l.Contains(Enacted, StringComparison.OrdinalIgnoreCase));
        if (enacted >= 0)
        {
            return enacted;
        }

        return IndexOfLine(lines, l => ChapterLine().IsMatch(l));
    }

    private static int IndexOfLine(IReadOnlyList<string> lines, Func<string, bool> predicate)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (predicate(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> Split(string raw)
    {
        var cleaned = Invisible().Replace(raw.Replace("\r\n", "\n").Replace('\r', '\n'), " ");
        var lines = new List<string>();

        foreach (var line in cleaned.Split('\n'))
        {
            var value = HorizontalSpace().Replace(line, " ").Trim();
            if (value.Length > 1
                && !IsRunningHead(value)
                && !Chrome().IsMatch(value)
                && !Repeats(value))
            {
                lines.Add(value);
            }
        }

        return lines;
    }

    private static int Trim(IReadOnlyList<string> lines, int from, int limit)
    {
        for (var i = from; i < limit; i++)
        {
            if (IsRunningHead(lines[i]))
            {
                return i;
            }
        }

        return limit;
    }

    private static (string Title, string Body) Separate(string tail)
    {
        var value = tail.Trim();

        foreach (var separator in Separators)
        {
            var at = value.IndexOf(separator, StringComparison.Ordinal);
            if (at is > 0 and < 200)
            {
                return (Clean(value[..at]), Clean(value[(at + separator.Length)..]));
            }
        }

        var period = value.IndexOf(". ", StringComparison.Ordinal);
        if (period is > 0 and < 200)
        {
            var rest = value[(period + 2)..];
            var looksLikeProse = rest.Length > MinimumBodyCharacters && !char.IsUpper(rest[0]);
            return looksLikeProse
                ? (Clean(value[..period]), Clean(rest))
                : (Clean(value[..period]), string.Empty);
        }

        return (Clean(value), string.Empty);
    }

    private static Dictionary<int, string> Chapters(IReadOnlyList<string> lines, int start)
    {
        var map = new Dictionary<int, string>();
        var current = string.Empty;

        for (var i = start; i < lines.Count; i++)
        {
            if (ChapterLine().IsMatch(lines[i]))
            {
                var name = Clean(lines[i])["CHAPTER".Length..];
                name = name.TrimStart(DashChars).TrimStart('-', ':', ' ').Trim();
                if (name.Contains(' ', StringComparison.Ordinal))
                {
                    current = name.ToUpperInvariant();
                }
            }

            map[i] = current;
        }

        return map;
    }

    private static string Compose(string chapter, string title) =>
        string.IsNullOrEmpty(chapter)
            ? title
            : string.IsNullOrEmpty(title) ? chapter : $"{chapter}, {title}";

    private static bool Repeats(string value)
    {
        for (var n = value.Length / 3; n >= 4; n--)
        {
            var unit = value[..n];
            if (unit.Trim().Length < n)
            {
                continue;
            }

            if (value.Length % n == 0 && string.Concat(Enumerable.Repeat(unit, value.Length / n)) == value)
            {
                return true;
            }
        }

        return false;
    }

    private static string Clean(string value)
    {
        var cleaned = AnySpace().Replace(value, " ").Trim();
        foreach (var marker in Bleed)
        {
            var at = cleaned.LastIndexOf(marker, StringComparison.Ordinal);
            if (at > 0)
            {
                cleaned = cleaned[..at].Trim();
            }
        }

        return cleaned;
    }

    private sealed record Mark(int Line, string Number, string Title, string Body);
}
