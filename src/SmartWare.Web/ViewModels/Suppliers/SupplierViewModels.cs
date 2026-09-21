using System.ComponentModel.DataAnnotations;
using SmartWare.Application.Catalog.Suppliers;

namespace SmartWare.Web.ViewModels.Suppliers;

public sealed class SupplierIndexViewModel
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public required SupplierPage Page { get; init; }
}

public sealed class SupplierFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã nhà cung cấp.")]
    [StringLength(30, ErrorMessage = "Mã nhà cung cấp không được vượt quá 30 ký tự.")]
    [RegularExpression("^[A-Za-z0-9._-]+$", ErrorMessage = "Mã chỉ được chứa chữ, số, dấu chấm, gạch ngang và gạch dưới.")]
    [Display(Name = "Mã nhà cung cấp")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên nhà cung cấp.")]
    [StringLength(200, ErrorMessage = "Tên nhà cung cấp không được vượt quá 200 ký tự.")]
    [Display(Name = "Tên nhà cung cấp")]
    public string Name { get; set; } = string.Empty;

    [StringLength(150, ErrorMessage = "Tên người liên hệ không được vượt quá 150 ký tự.")]
    [Display(Name = "Người liên hệ")]
    public string? ContactName { get; set; }

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [StringLength(30)]
    [Display(Name = "Số điện thoại")]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256)]
    public string? Email { get; set; }

    [StringLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự.")]
    [Display(Name = "Địa chỉ")]
    public string? Address { get; set; }

    [Display(Name = "Đang hoạt động")]
    public bool IsActive { get; set; } = true;
}
