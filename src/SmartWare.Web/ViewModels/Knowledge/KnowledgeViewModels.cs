using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using SmartWare.Application.AI.Rag;

namespace SmartWare.Web.ViewModels.Knowledge;

public sealed class KnowledgeIndexViewModel
{
    public required IReadOnlyList<KnowledgeDocumentSummary> Documents { get; init; }
}

public sealed class KnowledgeCreateViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tiêu đề tài liệu.")]
    [StringLength(300)]
    [Display(Name = "Tiêu đề")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập nhóm tài liệu.")]
    [StringLength(100)]
    [Display(Name = "Nhóm tài liệu")]
    public string Category { get; set; } = "Quy trình kho";

    [Required]
    [StringLength(50)]
    [Display(Name = "Phiên bản")]
    public string Version { get; set; } = "1.0";

    [StringLength(100_000)]
    [Display(Name = "Nội dung")]
    public string? Content { get; set; }

    [Display(Name = "Tệp văn bản")]
    public IFormFile? File { get; set; }

    [Display(Name = "Role được phép sử dụng")]
    public string[] AllowedRoles { get; set; } = [];
}
