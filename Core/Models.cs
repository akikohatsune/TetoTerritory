namespace TetoTerritory.CSharp.Core;

public enum ChatPersona
{
    Main,
    Secondary,
}

public sealed record LlmRuntimeProfile(
    string PersonaKey,
    string PersonaName,
    string Provider,
    string SystemPrompt,
    string? GeminiApiKey,
    string GeminiModel,
    string? GroqApiKey,
    string GroqModel,
    string? OpenAiApiKey,
    string OpenAiModel,
    string? OpenRouterApiKey,
    string OpenRouterModel);

public sealed record ImageInput(string MimeType, string DataB64);

public sealed class ChatMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public List<ImageInput> Images { get; init; } = new();
}

public sealed record MemoryMessage(string Role, string Content);
