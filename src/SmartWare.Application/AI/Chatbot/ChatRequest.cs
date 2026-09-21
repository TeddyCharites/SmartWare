using System.ComponentModel.DataAnnotations;

namespace SmartWare.Application.AI.Chatbot;

public sealed class ChatRequest
{
    public Guid? SessionId { get; init; }

    [Required(ErrorMessage = "Vui lòng nhập câu hỏi.")]
    [StringLength(1000, MinimumLength = 2, ErrorMessage = "Câu hỏi phải từ 2 đến 1.000 ký tự.")]
    public string Message { get; init; } = string.Empty;
}

public sealed record ChatUserContext(
    string UserId,
    string Role,
    IReadOnlyList<ChatHistoryMessage> RecentMessages);
