namespace SmartWare.Application.AI.Chatbot;

public sealed record ChatResponse(
    bool Success,
    string Answer,
    IReadOnlyList<string> Sources,
    bool IsAiGenerated,
    string? ErrorCode = null,
    Guid? SessionId = null,
    bool UsedRag = false,
    ChatReceiptDraft? Draft = null)
{
    public static ChatResponse Completed(
        string answer,
        IReadOnlyList<string> sources,
        bool isAiGenerated = true,
        bool usedRag = false) =>
        new(true, answer, sources, isAiGenerated, UsedRag: usedRag);

    public static ChatResponse Failure(string answer, string errorCode) =>
        new(false, answer, [], false, errorCode);
}
