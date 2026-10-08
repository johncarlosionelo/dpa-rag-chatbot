using Dpa.Rag.Core;
using Xunit;

namespace Dpa.Rag.Tests;

public sealed class QueryRouterTests
{
    private static QueryRouter Router() => new(Enumerable.Range(1, 45).Select(i => i.ToString()));

    [Theory]
    [InlineData("hello")]
    [InlineData("hi there")]
    [InlineData("good morning")]
    [InlineData("kamusta")]
    [InlineData("kumusta")]
    public void GreetsGreetings(string question) =>
        Assert.Equal(Intent.Greeting, Router().Route(question, [], 0).Intent);

    [Theory]
    [InlineData("who created you")]
    [InlineData("what can you do")]
    [InlineData("pwede mo ba akong tulungan")]
    [InlineData("are u an ai")]
    public void AnswersCapabilityQuestions(string question) =>
        Assert.Equal(Intent.Meta, Router().Route(question, [], 0).Intent);

    [Theory]
    [InlineData("section 12", "12")]
    [InlineData("what is section 12", "12")]
    [InlineData("sec 32", "32")]
    [InlineData("seksyon 29", "29")]
    [InlineData("can u expand 32", "32")]
    [InlineData("expand 12", "12")]
    public void PinsExplicitlyNamedSections(string question, string expected)
    {
        var route = Router().Route(question, [], 0);

        Assert.Equal(Intent.ExplicitSection, route.Intent);
        Assert.Equal(expected, route.SectionHints[0]);
    }

    [Fact]
    public void ExplicitNumberBeatsConversationMemory()
    {
        var route = Router().Route("can u expand 32", ["20"], 2);

        Assert.Equal(Intent.ExplicitSection, route.Intent);
        Assert.Equal("32", route.SectionHints[0]);
    }

    [Theory]
    [InlineData("what is 2 plus 2")]
    [InlineData("what is 12 times 3")]
    public void DoesNotTreatArithmeticAsASection(string question) =>
        Assert.NotEqual(Intent.ExplicitSection, Router().Route(question, [], 0).Intent);

    [Fact]
    public void ResolvesFollowUpAgainstMemory()
    {
        var route = Router().Route("expand that", ["12"], 2);

        Assert.Equal(Intent.FollowUp, route.Intent);
        Assert.Equal("12", route.SectionHints[0]);
        Assert.Contains("section 12", route.ResolvedQuestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownSectionNumberIsNotPinned() =>
        Assert.NotEqual(Intent.ExplicitSection, Router().Route("section 999", [], 0).Intent);

    [Theory]
    [InlineData("explain the whole act", true)]
    [InlineData("give me a summary", true)]
    [InlineData("buod ng batas", true)]
    [InlineData("what are the penalties for unauthorised access", false)]
    public void DetectsBroadQuestions(string question, bool expected) =>
        Assert.Equal(expected, QueryRouter.IsBroad(question));

    [Theory]
    [InlineData("explain it to me in tagalog", true)]
    [InlineData("kamusta", true)]
    [InlineData("bake sourdough bread", false)]
    [InlineData("what are the penalties", false)]
    public void DetectsFilipinoWithoutMatchingEnglishSubstrings(string question, bool expected) =>
        Assert.Equal(expected, QueryRouter.IsFilipino(question));

    [Theory]
    [InlineData("delikado ba may multa", "penalty")]
    [InlineData("ano ang karapatan", "rights")]
    [InlineData("what are the penalties", "what are the penalties")]
    public void ExpandsFilipinoLegalTerms(string question, string expected) =>
        Assert.Contains(expected, QueryRouter.Expand(question), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void ExpandsSignatoryAliases()
    {
        var expanded = QueryRouter.Expand("who signed this law");

        Assert.Contains("approved", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sgd", expanded, StringComparison.OrdinalIgnoreCase);
    }
}