using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.Identity;
using SmartWare.Domain.Constants;
using SmartWare.Domain.Enums;
using SmartWare.Web.ViewModels.Users;

namespace SmartWare.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("nguoi-dung")]
public sealed class UsersController(IUserManagementService userManagementService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var users = await userManagementService.GetUsersAsync(cancellationToken);
        return View(users);
    }

    [HttpGet("them")]
    public IActionResult Create() => View(new CreateUserViewModel());

    [HttpPost("them")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateUserViewModel model,
        CancellationToken cancellationToken)
    {
        if (!RoleNames.All.Contains(model.Role, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Role), "Vai trò không hợp lệ.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await userManagementService.CreateAsync(
            new CreateUserCommand(
                model.FullName,
                model.Email,
                model.PhoneNumber,
                model.Password,
                model.Role,
                GetCurrentUserId()),
            cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        TempData["SuccessMessage"] = "Đã tạo tài khoản thành công.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("sua/{id}")]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var user = await userManagementService.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return View(new EditUserViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Status = user.Status,
            Role = user.Role
        });
    }

    [HttpPost("sua/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        string id,
        EditUserViewModel model,
        CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!RoleNames.All.Contains(model.Role, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Role), "Vai trò không hợp lệ.");
        }

        var currentUserId = GetCurrentUserId();
        if (id == currentUserId &&
            (model.Status != UserStatus.Active || model.Role != RoleNames.Admin))
        {
            ModelState.AddModelError(
                string.Empty,
                "Bạn không thể tự khóa tài khoản hoặc tự bỏ quyền Admin.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await userManagementService.UpdateAsync(
            new UpdateUserCommand(
                model.Id,
                model.FullName,
                model.Email,
                model.PhoneNumber,
                model.Status,
                model.Role,
                currentUserId),
            cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        TempData["SuccessMessage"] = "Đã cập nhật tài khoản thành công.";
        return RedirectToAction(nameof(Index));
    }

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Không xác định được người dùng hiện tại.");

    private void AddErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }
}
