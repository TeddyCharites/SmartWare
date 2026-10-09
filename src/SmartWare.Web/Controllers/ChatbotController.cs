using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartWare.Application.AI.Chatbot;
using SmartWare.Application.Warehouse.Exports;
using SmartWare.Application.Warehouse.Imports;
using SmartWare.Domain.Constants;

namespace SmartWare.Web.Controllers;

[Authorize]
[EnableRateLimiting("chatbot")]
[Route("chatbot")]
public sealed class ChatbotController(
    IGeminiService geminiService,
    IChatHistoryService chatHistoryService,
    IChatDraftStore draftStore,
    IImportReceiptService importReceiptService,
    IExportReceiptService exportReceiptService) : Controller
{
    /// <summary>
    /// Creates the receipt the assistant drafted, after the user pressed "Xác nhận tạo phiếu".
    /// The draft comes from the server-side store (never from the browser), is bound to the
    /// current user and can be used once. Creation goes through the normal receipt services, so
    /// the receipt is validated, audited and starts in the "Chờ duyệt" state like any other.
    /// </summary>
    [HttpPost("drafts/{draftId:guid}/confirm")]
    [Authorize(Policy = AuthorizationPolicies.CreateReceipts)]
    [ValidateAntiForgeryToken]
    [DisableRateLimiting]
    public async Task<IActionResult> ConfirmDraft(Guid draftId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var draft = draftStore.Take(userId, draftId);
        if (draft is null)
        {
            return NotFound(new DraftConfirmation(false, "Bản nháp không tồn tại, đã hết hạn hoặc đã được xác nhận trước đó."));
        }

        if (!draft.CanConfirm)
        {
            draftStore.Save(userId, draft);
            return BadRequest(new DraftConfirmation(false, "Bản nháp còn lưu ý cần xử lý. Hãy bổ sung thông tin hoặc mở trong form để chỉnh sửa."));
        }

        var isImport = draft.Type == ChatDraftTypes.Import;
        var result = isImport
            ? await importReceiptService.CreateAsync(
                new CreateImportReceiptCommand(
                    draft.SupplierId ?? 0,
                    draft.WarehouseId,
                    draft.Lines.Select(line => new CreateImportReceiptLine(line.ProductId, line.Quantity, line.UnitCost)).ToArray(),
                    userId),
                cancellationToken)
            : await exportReceiptService.CreateAsync(
                new CreateExportReceiptCommand(
                    draft.OrderId,
                    draft.WarehouseId,
                    draft.Lines.Select(line => new CreateExportReceiptLine(line.ProductId, line.Quantity)).ToArray(),
                    userId),
                cancellationToken);
        if (!result.Succeeded)
        {
            // Keep the draft so the user can still open it in the form and fix it.
            draftStore.Save(userId, draft);
            return BadRequest(new DraftConfirmation(false, result.Errors.First()));
        }

        return Ok(new DraftConfirmation(
            true,
            result.Message ?? "Đã tạo phiếu.",
            Url.Action("Index", isImport ? "ImportReceipts" : "ExportReceipts")));
    }

    private sealed record DraftConfirmation(bool Success, string Message, string? ListUrl = null);

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
