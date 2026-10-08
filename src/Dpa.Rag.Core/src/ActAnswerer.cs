using System.Text;
using System.Text.RegularExpressions;

namespace Dpa.Rag.Core;

public sealed partial class ActAnswerer
{
    private const double MinimumRelevant = 0.18;
    private const int Retrieve = 8;

    private static readonly string[] Refusal =
    [
        "The Data Privacy Act of 2012 does not cover that. I only answer from the text of Republic Act No. 10379, and I found no section that addresses it.",
        "That is outside the scope of this Act, so I will not guess. Ask me about personal information, the data subject, the Commission, security of data, or penalties under the Act.",
    ];

    private readonly ActIndex _index;
    private readonly LlmClient _llm;

    public ActAnswerer(ActIndex index, LlmClient llm)
    {
        _index = index;
        _llm = llm;
    }

    public string Act => _index.Act;

    public int Sections => _index.Count;

    public async Task<Answer> AskAsync(string question, CancellationToken ct)
    {
        var hits = _index.Search(question, Retrieve);
        var best = hits.Count > 0 ? hits[0].Score : 0.0;

        if (best < MinimumRelevant)
        {
            return new Answer(Refusal[0], Refusal[1], [], null, 0.0);
        }

        var evidence = BuildEvidence(hits);
        var reply = await _llm.CompleteAsync(
        [
            new ChatTurn("system", SystemPrompt()),
            new ChatTurn("user", $"Question: {question}\n\n{evidence}"),
        ], ct);

        var (text, honest) = Sanitise(reply.Text, hits);
        var note = honest
            ? null
            : "I could not tie the Act to that question confidently, so I am treating it as outside the Act.";

        return new Answer(text, note, hits.Select(h => h.Section).ToList(), reply.Model, best);
    }

    private static string SystemPrompt() =>
        """
        You answer questions about the Data Privacy Act of 2012 of the Philippines, Republic Act No. 10379.

        Rules you must follow:
        1. Use only the numbered sections supplied with the question. Never use outside knowledge and never invent a section number.
        2. If the supplied sections do not answer the question, say plainly that the Act does not cover it. Do not speculate.
        3. Quote or closely paraphrase the Act, then name the section, for example "Section 12".
        4. Definitions live in Section 3. When the question asks what a term means, read Section 3 before concluding the Act does not cover it.
        5. If any supplied section can answer the question even partially, answer from it and say what it does not cover.
        6. Be brief and precise. No preamble, no summary of the Act, no offers of further help.
        7. Answer in the same language as the question.
        """;

    private static string BuildEvidence(IReadOnlyList<Scored> hits)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Sections:");
        foreach (var hit in hits)
        {
            builder.AppendLine();
            builder.AppendLine($"Section {hit.Section.Number}. {hit.Section.Title}");
            builder.AppendLine(hit.Section.Text);
        }

        return builder.ToString();
    }

    private static (string Text, bool Honest) Sanitise(string reply, IReadOnlyList<Scored> hits)
    {
        var allowed = hits.Select(h => h.Section.Number).ToHashSet(StringComparer.Ordinal);
        var invented = SectionReference()
            .Matches(reply)
            .Select(m => m.Groups[1].Value)
            .Where(n => !allowed.Contains(n))
            .ToArray();

        if (invented.Length == 0)
        {
            return (reply, true);
        }

        var stripped = SectionReference().Replace(reply, "the relevant provision");
        return (stripped, false);
    }

    [GeneratedRegex(@"\bSection\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SectionReference();
}

public sealed record Answer(
    string Text,
    string? Caveat,
    IReadOnlyList<EmbeddedSection> Sources,
    string? Model,
    double Score);
