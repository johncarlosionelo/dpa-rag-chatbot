using Dpa.Rag.Core;
using Xunit;

namespace Dpa.Rag.Tests;

public sealed class ActParserTests
{
    private const string Header = "Home About DPA Contact Us\n\nBe it enacted, by the Senate and House.\n\n";

    [Fact]
    public void ParsesNumberedSectionsInOrder()
    {
        var text = Header + """
            CHAPTER I GENERAL PROVISIONS
            SEC. 1. Short Title. - It is known as the Act.
            SEC. 2. Policy. - The State protects information here.
            """;

        var articles = new ActParser().Parse(text);

        Assert.Equal(["1", "2"], articles.Select(a => a.Number));
    }

    [Fact]
    public void AttachesChapterToTitle()
    {
        var text = Header + """
            CHAPTER IV RIGHTS OF THE DATA SUBJECT
            SEC. 16. Rights. - A person may be informed of processing done about them today.
            """;

        var article = Assert.Single(new ActParser().Parse(text));

        Assert.StartsWith("IV RIGHTS OF THE DATA SUBJECT", article.Title);
    }

    [Fact]
    public void SeparatesTitleFromBody()
    {
        var text = Header + """
            CHAPTER I GENERAL PROVISIONS
            SEC. 12. Lawful Processing. - Processing is permitted only under stated conditions.
            """;

        var article = Assert.Single(new ActParser().Parse(text));

        Assert.EndsWith("Lawful Processing.", article.Title);
        Assert.StartsWith("Processing is permitted", article.Text);
    }

    [Fact]
    public void DropsNavigationChrome()
    {
        var text = Header + """
            CHAPTER I GENERAL PROVISIONS
            Back To Top
            SEC. 4. Scope. - This Act covers processing done within the country by anyone.
            """;

        var article = Assert.Single(new ActParser().Parse(text));

        Assert.DoesNotContain("Back To Top", article.Text);
    }

    [Fact]
    public void MergesInlineBodyWithContinuationLines()
    {
        var text = Header + """
            CHAPTER VIII PENALTIES
            SEC. 29. Breach. - A person who knowingly accesses data without authority
            shall be fined and imprisoned under the penalty provided by law.
            """;

        var article = Assert.Single(new ActParser().Parse(text));

        Assert.StartsWith("A person who knowingly", article.Text);
        Assert.Contains("imprisoned", article.Text);
    }

    [Fact]
    public void ReturnsNothingWithoutBody()
    {
        Assert.Empty(new ActParser().Parse("front matter only"));
    }
}
