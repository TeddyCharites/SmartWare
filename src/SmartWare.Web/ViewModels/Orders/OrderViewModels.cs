using System.ComponentModel.DataAnnotations;
using SmartWare.Application.Sales.Orders;
using SmartWare.Domain.Enums;

namespace SmartWare.Web.ViewModels.Orders;

public sealed class OrderIndexViewModel
{
    public string? Search { get; init; }
    public OrderStatus? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public required OrderPage Page { get; init; }
}

public sealed class CreateOrderViewModel : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn khách hàng.")]
    [Display(Name = "Khách hàng")]
    public int CustomerId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập ngày đặt hàng.")]
    [Display(Name = "Ngày đặt hàng")]
    public DateTime OrderDate { get; set; } = DateTime.Now;

    public List<OrderLineViewModel> Lines { get; set; } = [new()];

    public IReadOnlyList<OrderLookupItem> Customers { get; set; } = [];
    public IReadOnlyList<OrderProductLookupItem> Products { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lines.Count == 0)
        {
            yield return new ValidationResult(
                "Đơn hàng phải có ít nhất một sản phẩm.",
                [nameof(Lines)]);
        }

        if (Lines.Count > 100)
        {
            yield return new ValidationResult(
                "Mỗi đơn hàng không được vượt quá 100 dòng sản phẩm.",
                [nameof(Lines)]);
        }

        if (Lines.Where(line => line.ProductId > 0)
            .GroupBy(line => line.ProductId)
            .Any(group => group.Count() > 1))
        {
            yield return new ValidationResult(
                "Mỗi sản phẩm chỉ được xuất hiện một lần trong đơn hàng.",
                [nameof(Lines)]);
        }
    }
}

public sealed class OrderLineViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn sản phẩm.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
    public int Quantity { get; set; } = 1;

    [Range(typeof(decimal), "0", "99999999999999", ErrorMessage = "Đơn giá không hợp lệ.")]
    public decimal UnitPrice { get; set; }
}

public sealed class ChangeOrderStatusViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [EnumDataType(typeof(OrderStatus))]
    public OrderStatus TargetStatus { get; set; }
}
