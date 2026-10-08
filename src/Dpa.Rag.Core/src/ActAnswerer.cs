using System.Text;
using System.Text.RegularExpressions;

namespace Dpa.Rag.Core;

public sealed partial class ActAnswerer
{
    private const double MinimumRelevant = 0.22;
    private const int Retrieve = 8;
    private const int EvidenceLimit = 5;
    private const int SynthesisLimit = 9;
    private const int SectionHead = 1400;
    private const int SectionTail = 500;
    private const int MemoryTurns = 4;

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex AnySpace();

    private static readonly Dictionary<string, int> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["isang"] = 1,
        ["isa"] = 1,
        ["dalawang"] = 2,
        ["dalawa"] = 2,
        ["tatlong"] = 3,
        ["tatlo"] = 3,
    };

    private readonly ActIndex _index;
    private readonly LlmClient _llm;
    private readonly QueryRouter _router;

    public ActAnswerer(ActIndex index, LlmClient llm)
    {
        _index = index;
        _llm = llm;
        _router = new QueryRouter(index.SectionNumbers);
    }

    public string Act => _index.Act;

    public int Sections => _index.Count;

    public string Backend => _index.Backend;

    public async Task<Answer> AskAsync(string question, IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        IReadOnlyList<ChatTurn> prior = history.Count > 0 ? [.. history.TakeLast(MemoryTurns)] : Array.Empty<ChatTurn>();
        var priorSections = prior
            .Select(t => SectionReference().Match(t.Content))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Reverse()
            .Distinct()
            .ToList();

        var priorTopic = prior
            .Where(t => t.Role is "user" or "assistant")
            .Reverse()
            .Select(t => t.Content)
            .FirstOrDefault(content => SectionReference().IsMatch(content) || content.Length > 12);

        var route = _router.Route(question, priorSections, prior.Count, priorTopic);

        var filipino = QueryRouter.IsFilipino(question);

        switch (route.Intent)
        {
            case Intent.Greeting:
                return new Answer(_router.Greet(prior.Count, filipino), null, [], null, 0.0, "greeting");

            case Intent.Meta:
                return new Answer(_router.DescribeScope(prior.Count, filipino), null, [], null, 0.0, "scope");
        }

        if (route.Intent == Intent.ExplicitSection ||
            (route.Intent == Intent.FollowUp && route.SectionHints.Count > 0))
        {
            return await AnswerPinnedAsync(
                route.SectionHints[0],
                route.ResolvedQuestion,
                route.Intent == Intent.FollowUp,
                filipino,
                ct);
        }

        var broad = QueryRouter.IsBroad(question);
        var hits = broad
            ? await _index.SkeletonAsync(SynthesisLimit, ct)
            : await _index.SearchAsync(QueryRouter.Expand(route.ResolvedQuestion), Retrieve, ct);
        var best = hits.Count > 0 ? hits[0].Score : 0.0;
        var genuine = hits.Where(h => !h.Pinned).Select(h => h.Score).DefaultIfEmpty(0.0).Max();

        if (_router.IsOutOfScope(question, genuine))
        {
            return Redirect(filipino);
        }

        if (hits.Count == 0 || best < MinimumRelevant)
        {
            return new Answer(
                _router.NoMatchText(_index.Act, filipino),
                null,
                [],
                null,
                best,
                "no_match");
        }

        var evidence = broad ? hits.Take(SynthesisLimit).ToList() : hits.Take(EvidenceLimit).ToList();
        evidence = broad ? evidence : [.. evidence.OrderByDescending(h => !h.Pinned)];
        var bestGenuine = evidence.Where(h => !h.Pinned).Select(h => h.Score).DefaultIfEmpty(0.0).Max();

        if (!evidence.Any(h => h.Pinned) && bestGenuine < MinimumRelevant)
        {
            return Redirect(filipino);
        }
        var context = BuildMemory(prior);

        var language = filipino ? "Tagalog" : "English";

        var reply = await _llm.CompleteAsync(
        [
            new ChatTurn("system", SystemPrompt()),
            new ChatTurn(
                "user",
                $"Language for your reply: {language}.\nQuestion: {question}\n\n{context}{BuildEvidence(evidence)}"),
        ], ct);

        var text = Clamp(Sanitise(reply.Text, evidence), question);

        var cited = CitedSections(text, evidence);

        return new Answer(
            text,
            null,
            cited.Count > 0 ? cited : [.. evidence.Take(2).Select(h => h.Section)],
            reply.Model,
            best,
            route.Intent == Intent.FollowUp ? "follow_up" : "grounded");
    }

    private async Task<Answer> AnswerPinnedAsync(string number, string question, bool followUp, bool filipino, CancellationToken ct)
    {
        var section = _index.ByNumber(number);

        if (section is null)
        {
            return new Answer($"There is no Section {number} in {_index.Act}.", null, [], null, 0.0, "no_match");
        }

        var pin = new List<Scored> { new(section, 1.0, 1.0, 1.0) { Pinned = true } };

        var reply = await _llm.CompleteAsync(
        [
            new ChatTurn("system", SystemPrompt()),
            new ChatTurn(
                "user",
                $"Language for your reply: {(filipino ? "Tagalog" : "English")}.\nQuestion: {question}\n\nThe user is following up on this specific section. Explain it in plain language, go deeper than a summary, and name the section.\n\n{BuildEvidence(pin)}"),
        ], ct);

        return new Answer(
            Clamp(Sanitise(reply.Text, pin), question),
            null,
            [.. pin.Select(h => h.Section)],
            reply.Model,
            1.0,
            followUp ? "follow_up" : "explicit_section");
    }

    private Answer Redirect(bool filipino)
    {
        var sections = SuggestedSections
            .Select(_index.ByNumber)
            .Where(section => section is not null)
            .Take(4)
            .Cast<EmbeddedSection>()
            .ToList();

        var reply = new Answer(
            _router.RedirectText(_index.Act, filipino),
            null,
            [],
            null,
            0.0,
            "redirect");

        return reply with { Suggestions = sections };
    }

    private static readonly string[] SuggestedSections = ["12", "13", "20", "25", "29"];

    private string BuildMemory(IReadOnlyList<ChatTurn> history)
    {
        if (history.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder("Earlier in this conversation:");
        foreach (var turn in history)
        {
            builder.AppendLine();
            builder.AppendLine($"{turn.Role}: {Clamp(turn.Content, 400)}");
        }

        builder.AppendLine();
        builder.AppendLine();
        return builder.ToString();
    }

    private static string Trim(string text)
    {
        if (text.Length <= SectionHead + SectionTail)
        {
            return text;
        }

        return string.Concat(
            text.AsSpan(0, SectionHead),
            "\n[... section continues, omitted for length ...]\n",
            text.AsSpan(text.Length - SectionTail, SectionTail));
    }

    private static string Clamp(string text, int max) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, max), "...");

    private static string BuildEvidence(IReadOnlyList<Scored> hits)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Sections:");

        foreach (var hit in hits.Take(EvidenceLimit))
        {
            builder.AppendLine();
            builder.AppendLine($"Section {hit.Section.Number}. {hit.Section.Title}");
            builder.AppendLine(hit.Pinned ? hit.Section.Text : Trim(hit.Section.Text));
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"(?<=\d)\.(?=\d)|(?<=[a-zA-Z])\.\s|!\s|\?\s", RegexOptions.Compiled)]
    private static partial Regex Boundary();

    [GeneratedRegex(
        @"(?:in|within|to|for)\s+(\d{1,2}|one|two|three|four|five|six|isang|isa|dalawang|dalawa|tatlong|tatlo)" +
        @"\s+(?:sentences?|short sentences?)" +
        @"|(\d{1,2}|one|two|three|four|five|isang|isa|dalawang|dalawa|tatlong|tatlo)\s+(?:lang|pangungusap|lamang)\b" +
        @"|\bin\s+(\d{1,2}|one|two|three|four|five)\s+words?\b" +
        @"|\b(isang|isa|dalawang|dalawa|tatlong|tatlo)\s+pangungusap\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LengthLimit();

    private static int Count(string value) =>
        Words.TryGetValue(value, out var mapped)
            ? mapped
            : int.TryParse(value, out var parsed) ? parsed : 0;

    internal static string Clamp(string answer, string question)
    {
        var limit = LengthLimit().Match(question);

        if (!limit.Success)
        {
            return answer;
        }

        var count = Count(limit.Groups[2].Success ? limit.Groups[2].Value : limit.Groups[1].Value);

        if (count is < 1 or > 12)
        {
            return answer;
        }

        var isWords = limit.Groups[3].Success;
        var parts = isWords
            ? [answer]
            : Boundary().Split(answer).Where(part => part.Trim().Length > 0).ToArray();

        if (parts.Length <= count)
        {
            return answer;
        }

        var sep = isWords ? string.Empty : ". ";

        if (isWords)
        {
            var words = answer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', words.Take(count));
        }

        return isWords ? string.Empty : string.Join(sep, parts.Take(count)) + ".";
    }

    [GeneratedRegex(@"\bSection\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlainSection();

    internal static List<EmbeddedSection> CitedSections(string answer, IReadOnlyList<Scored> evidence)
    {
        var order = PlainSection().Matches(answer)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        var cited = new List<EmbeddedSection>();

        foreach (var number in order)
        {
            var hit = evidence.FirstOrDefault(h => h.Section.Number == number);
            if (hit is not null)
            {
                cited.Add(hit.Section);
            }
        }

        return cited;
    }

    private static string Name(IReadOnlySet<string> allowed, Match match)
    {
        return string.Empty;
    }

    [GeneratedRegex(@"^\s*(?:-\s+|\d+[.)]\s+)?\**[A-Z]")]
    private static partial Regex OrphanLead();

    private static string Orphan(string line, IReadOnlySet<string> allowed)
    {
        return OrphanLead().IsMatch(line) ? line : string.Empty;
    }

    [GeneratedRegex(@"\*{2,}")]
    private static partial Regex Emphasis();

    internal static string Sanitise(string reply, IReadOnlyList<Scored> hits)
    {
        var allowed = hits.Select(h => h.Section.Number).ToHashSet(StringComparer.Ordinal);
        var flattened = Emphasis().Replace(reply, string.Empty).Trim();

        var cleaned = SectionReference().Replace(flattened, match =>
            allowed.Contains(match.Groups[1].Value) ? $"**{match.Value}**" : Name(allowed, match));

        var _ = allowed;

        var lines = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Orphan(line.Trim(), allowed))
            .Where(line => line.Length > 0)
            .ToList();

        return string.Join("\n", lines).Trim();
    }

    [GeneratedRegex(@"\bSection\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SectionReference();

    private static string SystemPrompt() =>
        """
        You answer questions about the Data Privacy Act of 2012 of the Philippines, Republic Act No. 10379.

        Rules you must follow:
        1. Use only the numbered sections supplied with the question. Never use outside knowledge and never invent a section number.
        2. If the supplied sections cannot answer the question at all, say plainly that the Act does not cover it in one sentence, then name the closest sections the Act does cover. Do not repeat yourself and do not add a second refusal.
        3. Quote or closely paraphrase the operative words, then name the section, for example "Section 12".
        4. Definitions live in Section 3. When asked what a term means, read Section 3 before concluding the Act does not cover it.
        5. Never write "the Act does not cover" or any refusal sentence in an answer where you already cited the answering sections. If one narrow part is missing, name that single gap at the end.
        6. Write like a competent lawyer explaining to an ordinary person. Plain language, short sentences, no jargon without a definition, no legalese padding.
        7. Keep the answer under 180 words. Cite at most the five most relevant sections and quote only the words that carry the rule.
        14. If several sections are supplied, use all of them to cover the Act's structure and name the different parts. Never answer a summary question from a single section.
        12. Read the whole supplied section before answering, including every lettered subsection. Do not claim the Act is silent when a supplied section does address the question.
        8. Answer entirely in the language named at the top of the message. Ignore the language used in earlier turns. If the question asks for another language, answer entirely in that language instead.
        13. Use markdown bold for the key legal terms and section names in your answer, for example **Section 12** or **consent**. Use bold at least twice in every answer. Do not bold whole sentences.
        9. If asked who signed or approved the Act, list every signatory named in the supplied section together with the office each one held. Do not name only the President.
        15. Never speculate about a scenario the Act does not address, and never say a question is outside the Act while still offering a related section as if it applied. If the Act does not cover the scenario, say so and stop.
        10. If asked which section is the most important, do not pick a winner. Say there is no single most important section, then name the ones that organise the rest, such as the definitions, the scope, the lawful processing criteria, the rights, and the penalties.
        11. If the question is about you rather than the Act, answer it in one short sentence and point the person back to the Act.
        """;
}

public sealed record Answer(
    string Text,
    string? Caveat,
    IReadOnlyList<EmbeddedSection> Sources,
    string? Model,
    double Score,
    string Kind)
{
    public IReadOnlyList<EmbeddedSection> Suggestions { get; init; } = [];
}
