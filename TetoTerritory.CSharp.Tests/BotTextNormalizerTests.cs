using TetoTerritory.CSharp.Core;
using Xunit;

namespace TetoTerritory.CSharp.Tests;

public sealed class BotTextNormalizerTests
{
    [Fact]
    public void SanitizeMentions_RewritesEveryoneAndHere()
    {
        var input = "hello @everyone and @here";
        var output = BotTextNormalizer.SanitizeMentions(input);

        Assert.NotEqual(input, output);
        Assert.Contains('\u200b', output);
        Assert.Contains("@\u200beveryone", output);
        Assert.Contains("@\u200bhere", output);
    }

    [Fact]
    public void NormalizeModelReply_UsesAnswerFieldFromJsonFence()
    {
        var input = "```json\n{\"answer\":\"final output\"}\n```";
        var output = BotTextNormalizer.NormalizeModelReply(input);
        Assert.Equal("final output", output);
    }

    [Fact]
    public void ExtractAnswerFromStructuredOutput_HandlesLeadingText()
    {
        var input = "notes {\"answer\":\"ok\"} trailing";
        var output = BotTextNormalizer.ExtractAnswerFromStructuredOutput(input);
        Assert.Equal("ok", output);
    }

    [Fact]
    public void NormalizeModelReply_HandlesSmartQuotesInStructuredOutput()
    {
        var input = "{\u201cstyle\u201d:\u201cdiscord_form_v1\u201d,\u201canswer\u201d:\u201cLine 1\\nLine 2\u201d,\u201cconfidence\u201d:0.9}";
        var output = BotTextNormalizer.NormalizeModelReply(input);
        Assert.Equal("Line 1\nLine 2", output);
    }

    [Fact]
    public void ExtractAnswerFromStructuredOutput_HandlesInvalidJsonWithAnswerField()
    {
        var input = "{\"style\":\"discord_form_v1\" \"answer\":\"ok\\nnext\"}";
        var output = BotTextNormalizer.ExtractAnswerFromStructuredOutput(input);
        Assert.Equal("ok\nnext", output);
    }

    [Fact]
    public void LatexToPlainMath_ConvertsCommonForms()
    {
        var input = @"Result: \frac{1}{2} and \sqrt{9} and x \neq y";
        var output = BotTextNormalizer.LatexToPlainMath(input);

        Assert.Contains("(1)/(2)", output);
        Assert.Contains("sqrt(9)", output);
        Assert.Contains("!=", output);
    }

    [Fact]
    public void NormalizeModelReply_PlainTextUnchanged()
    {
        var input = "just plain text";
        var output = BotTextNormalizer.NormalizeModelReply(input);
        Assert.Equal(input, output);
    }
}
