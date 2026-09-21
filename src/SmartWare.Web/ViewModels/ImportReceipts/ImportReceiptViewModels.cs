using System.ComponentModel.DataAnnotations;
using SmartWare.Application.Warehouse.Imports;
using SmartWare.Domain.Enums;

namespace SmartWare.Web.ViewModels.ImportReceipts;

public sealed class ImportReceiptIndexViewModel
{
    public string? Search { get; init; }
    public int? SupplierId { get; init; }
    public ReceiptStatus? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public required ImportReceiptPage Page { get; init; }
}

public sealed class CreateImportReceiptViewModel : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn nhà cung cấp.")]
    [Display(Name = "Nhà cung cấp")]
    public int SupplierId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kho nhận.")]
    [Display(Name = "Kho nhận")]
    public int WarehouseId { get; set; }

    public List<ImportReceiptLineViewModel> Lines { get; set; } = [new()];

    public IReadOnlyList<ImportLookupItem> Suppliers { get; set; } = [];
    public IReadOnlyList<ImportLookupItem> Warehouses { get; set; } = [];
    public IReadOnlyList<ImportProductLookupItem> Products { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lines.Count == 0)
        {
            yield return new ValidationResult(
                "Phiếu nhập phải có ít nhất một dòng hàng.",
                [nameof(Lines)]);
        }

        if (Lines.Count > 100)
        {
            yield return new ValidationResult(
                "Mỗi phiếu nhập không được vượt quá 100 dòng hàng.",
                [nameof(Lines)]);
        }

        var duplicateProduct = Lines
            .Where(line => line.ProductId > 0)
            .GroupBy(line => line.ProductId)
            .Any(group => group.Count() > 1);
        if (duplicateProduct)
        {
            yield return new ValidationResult(
                "Mỗi sản phẩm chỉ được xuất hiện một lần trong phiếu nhập.",
                [nameof(Lines)]);
        }
    }
}

public sealed class ImportReceiptLineViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn sản phẩm.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
    public int Quantity { get; set; } = 1;

    [Range(
        typeof(decimal),
        "0",
        "9999999999999999",
        MinimumIsExclusive = true,
        ErrorMessage = "Đơn giá phải lớn hơn 0.")]
    public decimal UnitCost { get; set; }
}

public sealed class ReviewImportReceiptViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Lý do từ chối không được vượt quá 1.000 ký tự.")]
    public string? RejectionReason { get; set; }
}

public sealed class CompleteImportReceiptViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
