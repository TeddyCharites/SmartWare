using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Identity;
using SmartWare.Domain.Constants;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure.Data;
using System.Text.Json;

namespace SmartWare.Infrastructure.Identity;

internal sealed class UserManagementService(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext) : IUserManagementService
{
    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await userManager.Users
            .AsNoTracking()
            .OrderBy(x => x.FullName)
            .ToListAsync(cancellationToken);

        var results = new List<UserSummary>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            results.Add(Map(user, roles.SingleOrDefault() ?? string.Empty));
        }

        return results;
    }

    public async Task<UserSummary?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return Map(user, roles.SingleOrDefault() ?? string.Empty);
    }

    public async Task<UserManagementResult> CreateAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RoleNames.All.Contains(command.Role, StringComparer.Ordinal))
        {
            return UserManagementResult.Failure("Vai trò không hợp lệ.");
        }

        var user = new ApplicationUser
        {
            UserName = command.Email.Trim(),
            Email = command.Email.Trim(),
            FullName = command.FullName.Trim(),
            PhoneNumber = NormalizeOptional(command.PhoneNumber),
            Status = UserStatus.Active,
            LockoutEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user, command.Password);
        if (!createResult.Succeeded)
        {
            return Failure(createResult);
        }

        var roleResult = await userManager.AddToRoleAsync(user, command.Role);
        if (!roleResult.Succeeded)
        {
            return Failure(roleResult);
        }

        await WriteAuditAsync(
            command.PerformedById,
            "Create",
            user.Id,
            null,
            new { user.FullName, user.Email, user.PhoneNumber, user.Status, command.Role },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return UserManagementResult.Success();
    }

    public async Task<UserManagementResult> UpdateAsync(
        UpdateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RoleNames.All.Contains(command.Role, StringComparer.Ordinal))
        {
            return UserManagementResult.Failure("Vai trò không hợp lệ.");
        }

        var user = await userManager.FindByIdAsync(command.Id);
        if (user is null)
        {
            return UserManagementResult.Failure("Không tìm thấy tài khoản.");
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Contains(RoleNames.Admin, StringComparer.Ordinal) &&
            (command.Role != RoleNames.Admin || command.Status != UserStatus.Active))
        {
            var admins = await userManager.GetUsersInRoleAsync(RoleNames.Admin);
            if (!admins.Any(x => x.Id != user.Id && x.Status == UserStatus.Active))
            {
                return UserManagementResult.Failure(
                    "Không thể vô hiệu hóa hoặc đổi quyền của Admin đang hoạt động cuối cùng.");
            }
        }

        var oldValues = new
        {
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.Status,
            Role = currentRoles.SingleOrDefault()
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var email = command.Email.Trim();
        var emailResult = await userManager.SetEmailAsync(user, email);
        if (!emailResult.Succeeded)
        {
            return Failure(emailResult);
        }

        var userNameResult = await userManager.SetUserNameAsync(user, email);
        if (!userNameResult.Succeeded)
        {
            return Failure(userNameResult);
        }

        user.FullName = command.FullName.Trim();
        user.PhoneNumber = NormalizeOptional(command.PhoneNumber);
        user.Status = command.Status;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.LockoutEnabled = true;
        user.LockoutEnd = command.Status == UserStatus.Active
            ? null
            : DateTimeOffset.MaxValue;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Failure(updateResult);
        }

        if (currentRoles.Count > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                return Failure(removeResult);
            }
        }

        var addRoleResult = await userManager.AddToRoleAsync(user, command.Role);
        if (!addRoleResult.Succeeded)
        {
            return Failure(addRoleResult);
        }

        await WriteAuditAsync(
            command.PerformedById,
            "Update",
            user.Id,
            oldValues,
            new { user.FullName, user.Email, user.PhoneNumber, user.Status, command.Role },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return UserManagementResult.Success();
    }

    private static UserSummary Map(ApplicationUser user, string role) =>
        new(
            user.Id,
            user.FullName,
            user.Email ?? string.Empty,
            user.PhoneNumber,
            user.Status,
            role);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static UserManagementResult Failure(IdentityResult result) =>
        UserManagementResult.Failure(result.Errors.Select(x => x.Description));

    private async Task WriteAuditAsync(
        string performedById,
        string action,
        string entityId,
        object? oldValues,
        object newValues,
        CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = performedById,
            Action = action,
            Module = "UserManagement",
            EntityType = nameof(ApplicationUser),
            EntityId = entityId,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = JsonSerializer.Serialize(newValues),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
