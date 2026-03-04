using TetoTerritory.CSharp.Core;
using Xunit;

namespace TetoTerritory.CSharp.Tests;

public sealed class PersonaKeywordMatcherTests
{
    [Fact]
    public void ParseMarkdownKeywords_ParsesListAndSkipsComments()
    {
        var markdown =
            "# header\n" +
            "- Persona2\n" +
            "* strict mode\n" +
            "1. `focus mode`\n" +
            "\n" +
            "# ignored\n";

        var keywords = PersonaKeywordMatcher.ParseMarkdownKeywords(markdown);

        Assert.Equal(3, keywords.Count);
        Assert.Contains("persona2", keywords);
        Assert.Contains("strict mode", keywords);
        Assert.Contains("focus mode", keywords);
    }

    [Fact]
    public void ContainsKeyword_MatchesCaseInsensitivePhrase()
    {
        var keywords = new[] { "strict mode", "persona2" };

        var matched = PersonaKeywordMatcher.ContainsKeyword(
            "Please switch to STRICT MODE for this answer.",
            keywords);

        Assert.True(matched);
    }

    [Fact]
    public void ContainsKeyword_NoMatch_ReturnsFalse()
    {
        var keywords = new[] { "strict mode", "persona2" };

        var matched = PersonaKeywordMatcher.ContainsKeyword(
            "normal response please",
            keywords);

        Assert.False(matched);
    }
}
