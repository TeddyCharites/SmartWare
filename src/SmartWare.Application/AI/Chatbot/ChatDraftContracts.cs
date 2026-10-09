namespace SmartWare.Application.AI.Chatbot;

public static class ChatDraftTypes
{
    public const string Import = "import";
    public const string Export = "export";
}

public sealed record ChatReceiptDraftLine(
    int ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasure,
    int Quantity,
    decimal UnitCost,
    int? AvailableQuantity);

/// <summary>
/// A receipt prepared by the assistant but not yet created. It is created only when the user
/// confirms it (or opens it in the regular form), through the normal receipt services.
/// </summary>
public sealed record ChatReceiptDraft(
    Guid Id,
    string Type,
    int WarehouseId,
    string WarehouseName,
    int? SupplierId,
    string? SupplierName,
    int? OrderId,
    string? OrderNumber,
    IReadOnlyList<ChatReceiptDraftLine> Lines,
    decimal TotalValue,
    IReadOnlyList<string> Warnings,
    bool CanConfirm);

/// <summary>
/// Short-lived, per-user storage for assistant drafts. A draft is visible only to the user it
/// was prepared for and can be taken (confirmed) only once.
/// </summary>
public interface IChatDraftStore
{
    void Save(string userId, ChatReceiptDraft draft);

    ChatReceiptDraft? Get(string userId, Guid draftId);

    /// <summary>Atomically removes and returns the draft, so a double click cannot create two receipts.</summary>
    ChatReceiptDraft? Take(string userId, Guid draftId);
}
