using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartWare.Application.AI.Chatbot;
using SmartWare.Domain.Constants;

namespace SmartWare.Web.Controllers;

[Authorize]
[EnableRateLimiting("chatbot")]
[Route("chatbot")]
public sealed class ChatbotController(
    IGeminiService geminiService,
    IChatHistoryService chatHistoryService) : Controller
{
    [HttpGet("status")]
    [DisableRateLimiting]
    public IActionResult Status() => Ok(new { configured = geminiService.IsConfigured });

    [HttpGet("history")]
    [DisableRateLimiting]
    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        var role = GetCurrentRole();
        return role is null
            ? Forbid()
            : Ok(await chatHistoryService.GetSessionsAsync(
                GetCurrentUserId(),
                role,
                cancellationToken));
    }

    [HttpGet("history/{sessionId:guid}")]
    [DisableRateLimiting]
    public async Task<IActionResult> HistoryDetails(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var role = GetCurrentRole();
        if (role is null)
        {
            return Forbid();
        }

        var session = await chatHistoryService.GetSessionAsync(
            GetCurrentUserId(),
            role,
            sessionId,
            cancellationToken);
        return session is null ? NotFound() : Ok(session);
    }

    [HttpDelete("history/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    [DisableRateLimiting]
    public async Task<IActionResult> DeleteHistory(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var role = GetCurrentRole();
        if (role is null)
        {
            return Forbid();
        }

        var deleted = await chatHistoryService.DeleteSessionAsync(
            GetCurrentUserId(),
            role,
            sessionId,
            cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("ask")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var message = ModelState.Values
                .SelectMany(entry => entry.Errors)
                .Select(error => error.ErrorMessage)
                .FirstOrDefault(error => !string.IsNullOrWhiteSpace(error))
                ?? "Câu hỏi không hợp lệ.";
            return BadRequest(ChatResponse.Failure(message, "invalid_request"));
        }

        var role = GetCurrentRole();
        if (role is null)
        {
            return Forbid();
        }

        var userId = GetCurrentUserId();
        IReadOnlyList<ChatHistoryMessage> recentMessages = [];
        if (request.SessionId.HasValue)
        {
            var savedMessages = await chatHistoryService.GetRecentMessagesAsync(
                userId,
                role,
                request.SessionId.Value,
                8,
                cancellationToken);
            if (savedMessages is null)
            {
                return NotFound(ChatResponse.Failure(
                    "Không tìm thấy cuộc trò chuyện này.",
                    "chat_session_not_found"));
            }

            recentMessages = savedMessages;
        }

        var response = await geminiService.AskAsync(
            request,
            new ChatUserContext(userId, role, recentMessages),
            cancellationToken);
        if (response.Success)
        {
            var sessionId = await chatHistoryService.AppendExchangeAsync(
                userId,
                role,
                request.SessionId,
                request.Message,
                response,
                cancellationToken);
            return Ok(response with { SessionId = sessionId });
        }

        var statusCode = response.ErrorCode switch
        {
            "gemini_not_configured" => StatusCodes.Status503ServiceUnavailable,
            "gemini_rate_limited" => StatusCodes.Status429TooManyRequests,
            "gemini_timeout" => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway
        };
        return StatusCode(statusCode, response);
    }

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Không xác định được người dùng hiện tại.");

    private string? GetCurrentRole()
    {
        if (User.IsInRole(RoleNames.Admin))
        {
            return RoleNames.Admin;
        }

        if (User.IsInRole(RoleNames.Manager))
        {
            return RoleNames.Manager;
        }

        return User.IsInRole(RoleNames.Employee) ? RoleNames.Employee : null;
    }
}
