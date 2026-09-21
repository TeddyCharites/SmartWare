using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using SmartWare.Application.Catalog.Products;

namespace SmartWare.Web.ViewModels.Products;

public sealed class ProductIndexViewModel
{
    public string? Search { get; init; }
    public int? CategoryId { get; init; }
    public int? SupplierId { get; init; }
    public bool? IsActive { get; init; }
    public required ProductPage Page { get; init; }
}

public sealed class ProductFormViewModel : IValidatableObject
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập SKU.")]
    [StringLength(50, ErrorMessage = "SKU không được vượt quá 50 ký tự.")]
    [RegularExpression("^[A-Za-z0-9._-]+$", ErrorMessage = "SKU chỉ được chứa chữ, số, dấu chấm, gạch ngang và gạch dưới.")]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
    [StringLength(200, ErrorMessage = "Tên sản phẩm không được vượt quá 200 ký tự.")]
    [Display(Name = "Tên sản phẩm")]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục.")]
    [Display(Name = "Danh mục")]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn nhà cung cấp.")]
    [Display(Name = "Nhà cung cấp")]
    public int SupplierId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập đơn vị tính.")]
    [StringLength(50, ErrorMessage = "Đơn vị tính không được vượt quá 50 ký tự.")]
    [Display(Name = "Đơn vị tính")]
    public string UnitOfMeasure { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Hình ảnh sản phẩm")]
    public string? ImageUrl { get; set; }

    [Display(Name = "Tải ảnh sản phẩm")]
    public IFormFile? ImageFile { get; set; }

    [Display(Name = "Xóa ảnh hiện tại")]
    public bool RemoveImage { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999", ErrorMessage = "Giá bán phải lớn hơn hoặc bằng 0.")]
    [Display(Name = "Giá bán")]
    public decimal SellingPrice { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Tồn tối thiểu phải lớn hơn hoặc bằng 0.")]
    [Display(Name = "Tồn tối thiểu")]
    public int MinimumStock { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Tồn tối đa phải lớn hơn hoặc bằng 0.")]
    [Display(Name = "Tồn tối đa")]
    public int MaximumStock { get; set; }

    [StringLength(2000, ErrorMessage = "Mô tả không được vượt quá 2.000 ký tự.")]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Display(Name = "Đang kinh doanh")]
    public bool IsActive { get; set; } = true;

    public IReadOnlyList<LookupItem> Categories { get; set; } = [];
    public IReadOnlyList<LookupItem> Suppliers { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ImageFile is not null && RemoveImage)
        {
            yield return new ValidationResult(
                "Không thể vừa tải ảnh mới vừa chọn xóa ảnh.",
                [nameof(ImageFile), nameof(RemoveImage)]);
        }

        if (MaximumStock < MinimumStock)
        {
            yield return new ValidationResult(
                "Tồn tối đa phải lớn hơn hoặc bằng tồn tối thiểu.",
                [nameof(MaximumStock)]);
        }
    }
}
