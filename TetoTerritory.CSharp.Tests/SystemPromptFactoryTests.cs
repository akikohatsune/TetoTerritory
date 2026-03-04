using TetoTerritory.CSharp.Core;
using Xunit;

namespace TetoTerritory.CSharp.Tests;

public sealed class SystemPromptFactoryTests
{
    [Fact]
    public void Build_PrependsAuthoritativeUtcBlockAndKeepsSystemPrompt()
    {
        var now = new DateTimeOffset(2026, 3, 4, 21, 30, 5, 123, TimeSpan.FromHours(7));
        var systemPrompt = "Always answer in plain text.";

        var prompt = SystemPromptFactory.Build(systemPrompt, now);

        Assert.Contains("Authoritative UTC Time: 2026-03-04T14:30:05.123Z", prompt);
        Assert.Contains("Authoritative Current Year: 2026", prompt);
        Assert.EndsWith(systemPrompt, prompt);
    }

    [Fact]
    public void Build_WithEmptySystemPrompt_ReturnsOnlyTimeBlock()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var prompt = SystemPromptFactory.Build("   ", now);

        Assert.Equal(
            "Authoritative UTC Time: 2026-01-01T00:00:00.000Z\nAuthoritative Current Year: 2026",
            prompt);
    }
}
