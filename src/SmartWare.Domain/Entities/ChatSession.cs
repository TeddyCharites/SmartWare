namespace SmartWare.Domain.Entities;

public sealed class ChatSession
{
    public Guid ChatSessionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AccessRole { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
