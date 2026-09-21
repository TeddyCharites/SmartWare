using System.ComponentModel.DataAnnotations;
using SmartWare.Application.Catalog.Customers;

namespace SmartWare.Web.ViewModels.Customers;

public sealed class CustomerIndexViewModel
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public required CustomerPage Page { get; init; }
}

public sealed class CustomerFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã khách hàng.")]
    [StringLength(30, ErrorMessage = "Mã khách hàng không được vượt quá 30 ký tự.")]
    [RegularExpression("^[A-Za-z0-9._-]+$", ErrorMessage = "Mã chỉ được chứa chữ, số, dấu chấm, gạch ngang và gạch dưới.")]
    [Display(Name = "Mã khách hàng")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên khách hàng.")]
    [StringLength(200, ErrorMessage = "Tên khách hàng không được vượt quá 200 ký tự.")]
    [Display(Name = "Tên khách hàng")]
    public string Name { get; set; } = string.Empty;

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
