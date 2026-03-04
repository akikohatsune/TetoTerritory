namespace TetoTerritory.CSharp.Core;

internal sealed class MainPersonaLlmClient
{
    private readonly LlmClient _engine;
    private readonly Settings _settings;

    public MainPersonaLlmClient(LlmClient engine, Settings settings)
    {
        _engine = engine;
        _settings = settings;
    }

    public Task<string> GenerateAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        return _engine.GenerateAsync(messages, _settings.MainPersonaProfile, cancellationToken);
    }
}
