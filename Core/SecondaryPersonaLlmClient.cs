namespace TetoTerritory.CSharp.Core;

internal sealed class SecondaryPersonaLlmClient
{
    private readonly LlmClient _engine;

    public SecondaryPersonaLlmClient(LlmClient engine)
    {
        _engine = engine;
    }

    public Task<string> GenerateAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmRuntimeProfile profile,
        CancellationToken cancellationToken = default)
    {
        return _engine.GenerateAsync(messages, profile, cancellationToken);
    }
}
