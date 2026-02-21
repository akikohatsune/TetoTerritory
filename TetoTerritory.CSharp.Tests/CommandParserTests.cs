using TetoTerritory.CSharp.Core;
using Xunit;

namespace TetoTerritory.CSharp.Tests;

public sealed class CommandParserTests
{
    [Fact]
    public void TryParsePrefixedCommand_WithArgs_ParsesCommandAndArgs()
    {
        var parser = new CommandParser("!");
        var ok = parser.TryParsePrefixedCommand("!chat hello teto", out var commandName, out var args);

        Assert.True(ok);
        Assert.Equal("chat", commandName);
        Assert.Equal("hello teto", args);
    }

    [Fact]
    public void TryParsePrefixedCommand_NoArgs_ParsesCommandOnly()
    {
        var parser = new CommandParser("!");
        var ok = parser.TryParsePrefixedCommand("!provider", out var commandName, out var args);

        Assert.True(ok);
        Assert.Equal("provider", commandName);
        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void TryParsePrefixedCommand_WrongPrefix_ReturnsFalse()
    {
        var parser = new CommandParser("!");
        var ok = parser.TryParsePrefixedCommand("/chat hi", out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ExtractInlineReplayId_ValidPattern_ReturnsId()
    {
        var parser = new CommandParser("!");
        var replayId = parser.ExtractInlineReplayId("!replayteto123");
        Assert.Equal(123, replayId);
    }

    [Fact]
    public void TryParseFirstToken_WithRemainder_ParsesBoth()
    {
        var ok = CommandParser.TryParseFirstToken("@user reason text", out var firstToken, out var remainder);

        Assert.True(ok);
        Assert.Equal("@user", firstToken);
        Assert.Equal("reason text", remainder);
    }

    [Fact]
    public void ExtractUserId_PrefersMentionedUserList()
    {
        var userId = CommandParser.ExtractUserId("<@999>", new[] { 123UL, 456UL });
        Assert.Equal(123UL, userId);
    }

    [Theory]
    [InlineData("<@123>", 123UL)]
    [InlineData("<@!456>", 456UL)]
    [InlineData("789", 789UL)]
    public void ExtractUserId_ParsesTokenForms(string token, ulong expected)
    {
        var userId = CommandParser.ExtractUserId(token, Array.Empty<ulong>());
        Assert.Equal(expected, userId);
    }

    [Fact]
    public void ExtractUserId_InvalidToken_ReturnsNull()
    {
        var userId = CommandParser.ExtractUserId("not-a-user", Array.Empty<ulong>());
        Assert.Null(userId);
    }
}
