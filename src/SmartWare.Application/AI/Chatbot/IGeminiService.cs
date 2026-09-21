namespace SmartWare.Application.AI.Chatbot;

public interface IGeminiService
{
    bool IsConfigured { get; }

    Task<ChatResponse> AskAsync(
        ChatRequest request,
        ChatUserContext userContext,
        CancellationToken cancellationToken = default);
}
