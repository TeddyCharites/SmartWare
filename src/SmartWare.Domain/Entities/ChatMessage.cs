namespace SmartWare.Domain.Entities;

public sealed class ChatMessage
{
    public long ChatMessageId { get; set; }
    public Guid ChatSessionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? SourcesJson { get; set; }
    public bool IsAiGenerated { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ChatSession Session { get; set; } = null!;
}
