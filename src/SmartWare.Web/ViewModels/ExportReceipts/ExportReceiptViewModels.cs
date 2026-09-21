using System.ComponentModel.DataAnnotations;
using SmartWare.Application.Warehouse.Exports;
using SmartWare.Domain.Enums;

namespace SmartWare.Web.ViewModels.ExportReceipts;

public sealed class ExportReceiptIndexViewModel
{
    public string? Search { get; init; }
    public int? CustomerId { get; init; }
    public ReceiptStatus? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public required ExportReceiptPage Page { get; init; }
}

public sealed class CreateExportReceiptViewModel : IValidatableObject
{
    [Display(Name = "Đơn hàng")]
    public int? OrderId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kho xuất.")]
    [Display(Name = "Kho xuất")]
    public int WarehouseId { get; set; }

    public List<ExportReceiptLineViewModel> Lines { get; set; } = [new()];

    public IReadOnlyList<ExportLookupItem> Warehouses { get; set; } = [];
    public IReadOnlyList<ExportProductLookupItem> Products { get; set; } = [];
    public IReadOnlyList<ExportOrderLookupItem> Orders { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lines.Count == 0)
        {
            yield return new ValidationResult(
                "Phiếu xuất phải có ít nhất một dòng hàng.",
                [nameof(Lines)]);
        }

        if (Lines.Count > 100)
        {
            yield return new ValidationResult(
                "Mỗi phiếu xuất không được vượt quá 100 dòng hàng.",
                [nameof(Lines)]);
        }

        var hasDuplicate = Lines
            .Where(line => line.ProductId > 0)
            .GroupBy(line => line.ProductId)
            .Any(group => group.Count() > 1);
        if (hasDuplicate)
        {
            yield return new ValidationResult(
                "Mỗi sản phẩm chỉ được xuất hiện một lần trong phiếu xuất.",
                [nameof(Lines)]);
        }
    }
}

public sealed class ExportReceiptLineViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn sản phẩm.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
    public int Quantity { get; set; } = 1;
}

public sealed class ReviewExportReceiptViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Lý do từ chối không được vượt quá 1.000 ký tự.")]
    public string? RejectionReason { get; set; }
}

public sealed class CompleteExportReceiptViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
