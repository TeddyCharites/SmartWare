using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.AI.Rag;
using SmartWare.Domain.Constants;
using SmartWare.Web.ViewModels.Knowledge;

namespace SmartWare.Web.Controllers;

[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Manager)]
[Route("kho-tri-thuc")]
public sealed class KnowledgeController(IKnowledgeService knowledgeService) : Controller
{
    private const long MaximumFileSize = 2 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions =
        new([".txt", ".md", ".csv"], StringComparer.OrdinalIgnoreCase);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new KnowledgeIndexViewModel
        {
            Documents = await knowledgeService.GetDocumentsAsync(cancellationToken)
        });

    [HttpGet("xem/{id:int}")]
    public async Task<IActionResult> Details(
        int id,
        CancellationToken cancellationToken)
    {
        var role = GetCurrentRole();
        if (role is null)
        {
            return Forbid();
        }

        var document = await knowledgeService.GetDocumentAsync(id, role, cancellationToken);
        return document is null ? NotFound() : View(document);
    }

    [HttpGet("them")]
    public IActionResult Create() => View(new KnowledgeCreateViewModel
    {
        AllowedRoles = [RoleNames.Admin, RoleNames.Manager, RoleNames.Employee]
    });

    [HttpPost("them")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        KnowledgeCreateViewModel model,
        CancellationToken cancellationToken)
    {
        var content = model.Content?.Trim() ?? string.Empty;
        var sourceFile = "Nhập trực tiếp";
        if (model.File is not null && model.File.Length > 0)
        {
            var extension = Path.GetExtension(model.File.FileName);
            if (!AllowedExtensions.Contains(extension))
            {
                ModelState.AddModelError(nameof(model.File), "Chỉ hỗ trợ tệp .txt, .md hoặc .csv.");
            }
            else if (model.File.Length > MaximumFileSize)
            {
                ModelState.AddModelError(nameof(model.File), "Tệp không được vượt quá 2 MB.");
            }
            else
            {
                using var reader = new StreamReader(model.File.OpenReadStream());
                content = await reader.ReadToEndAsync(cancellationToken);
                sourceFile = Path.GetFileName(model.File.FileName);
            }
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            ModelState.AddModelError(nameof(model.Content), "Hãy nhập nội dung hoặc chọn một tệp văn bản.");
        }

        if (model.AllowedRoles.Length == 0)
        {
            ModelState.AddModelError(nameof(model.AllowedRoles), "Hãy chọn ít nhất một role.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await knowledgeService.CreateAsync(
            new CreateKnowledgeDocumentCommand(
                model.Title,
                model.Category,
                model.Version,
                sourceFile,
                content,
                model.AllowedRoles,
                GetCurrentUserId()),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        TempData["SuccessMessage"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("trang-thai/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(
        int id,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var result = await knowledgeService.SetActiveAsync(id, isActive, cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("lap-chi-muc")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reindex(
        int? id,
        CancellationToken cancellationToken)
    {
        var result = await knowledgeService.ReindexAsync(id, cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Index));
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

        return User.IsInRole(RoleNames.Manager) ? RoleNames.Manager : null;
    }
}
