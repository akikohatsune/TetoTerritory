namespace TetoTerritory.CSharp.Core;

public sealed record ImageInput(string MimeType, string DataB64);

public sealed class ChatMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public List<ImageInput> Images { get; init; } = new();
}

public sealed record MemoryMessage(string Role, string Content);
