namespace SmartWare.Application.AI.Chatbot;

public sealed record ChatHistoryMessage(
    long Id,
    string Role,
    string Content,
    IReadOnlyList<string> Sources,
    bool IsAiGenerated,
    DateTimeOffset CreatedAt);

public sealed record ChatSessionSummary(
    Guid Id,
    string Title,
    DateTimeOffset UpdatedAt,
    int MessageCount);

public sealed record ChatSessionDetails(
    Guid Id,
    string Title,
    IReadOnlyList<ChatHistoryMessage> Messages);

public interface IChatHistoryService
{
    Task<IReadOnlyList<ChatSessionSummary>> GetSessionsAsync(
        string userId,
        string accessRole,
        CancellationToken cancellationToken = default);

    Task<ChatSessionDetails?> GetSessionAsync(
        string userId,
        string accessRole,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatHistoryMessage>?> GetRecentMessagesAsync(
        string userId,
        string accessRole,
        Guid sessionId,
        int take,
        CancellationToken cancellationToken = default);

    Task<Guid> AppendExchangeAsync(
        string userId,
        string accessRole,
        Guid? sessionId,
        string question,
        ChatResponse response,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteSessionAsync(
        string userId,
        string accessRole,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
