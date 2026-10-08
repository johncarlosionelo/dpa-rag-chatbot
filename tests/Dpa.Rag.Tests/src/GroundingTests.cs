using Dpa.Rag.Core;
using Xunit;

namespace Dpa.Rag.Tests;

public sealed class GroundingTests
{
    private static Scored Section(string number) =>
        new(new EmbeddedSection(number, $"Title {number}", "Body", []), 1.0, 1.0, 1.0);

    private static readonly Scored[] Evidence = [Section("12"), Section("29"), Section("3")];

    [Fact]
    public void KeepsCitationsThatWereSupplied()
    {
        var result = ActAnswerer.Sanitise("See Section 12 and Section 29.", Evidence);

        Assert.Contains("Section 12", result, StringComparison.Ordinal);
        Assert.Contains("Section 29", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovesCitationsThatWereNeverSupplied()
    {
        var result = ActAnswerer.Sanitise("Section 12 applies. Section 999 also applies.", Evidence);

        Assert.Contains("Section 12", result, StringComparison.Ordinal);
        Assert.DoesNotContain("999", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BoldsSuppliedCitationsSoTheUiHasAnchors()
    {
        var result = ActAnswerer.Sanitise("Under Section 29 the penalty applies.", Evidence);

        Assert.Contains("**Section 29**", result, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotProduceQuadrupleAsterisks()
    {
        var result = ActAnswerer.Sanitise("**Section 12** applies.", Evidence);

        Assert.DoesNotContain("****", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsOnlySectionsTheAnswerActuallyCited()
    {
        var answer = "Under Section 29 the penalty applies.";

        var cited = ActAnswerer.CitedSections(answer, Evidence);

        Assert.Equal(["29"], cited.Select(s => s.Number));
    }

    [Fact]
    public void ReportsNothingWhenTheAnswerCitedNothing()
    {
        var cited = ActAnswerer.CitedSections("There is no fixed period.", Evidence);

        Assert.Empty(cited);
    }

    [Theory]
    [InlineData("explain section 12 in 2 sentences", 2)]
    [InlineData("explain section 12 in three sentences", 3)]
    [InlineData("ipaliwanag mo sa dalawang pangungusap", 2)]
    [InlineData("explain in 3 sentences", 3)]
    public void ClampsToTheSentenceCountRequested(string question, int allowed)
    {
        var verbose = "One. Two. Three. Four. Five.";

        var result = ActAnswerer.Clamp(verbose, question);

        Assert.True(
            Count(result) <= allowed,
            $"expected at most {allowed} sentences but got {Count(result)}: {result}");
    }

    [Fact]
    public void LeavesTheAnswerAloneWhenNoLengthWasRequested()
    {
        var answer = "One. Two. Three.";

        Assert.Equal(answer, ActAnswerer.Clamp(answer, "what are the penalties"));
    }

    [Fact]
    public void DoesNotSplitInsideMoneyAmounts()
    {
        var answer = "The fine is Php2,000,000.00 and the jail term is one to three years.";

        Assert.Equal(1, Count(ActAnswerer.Clamp(answer, "explain in 1 sentence")));
        Assert.Equal(2, Count(ActAnswerer.Clamp(answer, "explain in 2 sentences")));
    }

    private static int Count(string text) =>
        System.Text.RegularExpressions.Regex
            .Split(text, @"(?<=\d)\.(?=\d)|(?<=[a-zA-Z])\.\s|!\s|\?\s")
            .Count(part => part.Trim().Length > 0);
}