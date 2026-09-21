using System.ComponentModel.DataAnnotations;
using SmartWare.Application.Catalog.Categories;
using SmartWare.Application.Common;

namespace SmartWare.Web.ViewModels.Categories;

public sealed class CategoryIndexViewModel
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public required PagedResult<CategoryListItem> Results { get; init; }
}

public sealed class CategoryFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên danh mục.")]
    [StringLength(150, ErrorMessage = "Tên danh mục không được vượt quá 150 ký tự.")]
    [Display(Name = "Tên danh mục")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Mô tả không được vượt quá 1.000 ký tự.")]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Display(Name = "Đang hoạt động")]
    public bool IsActive { get; set; } = true;
}
