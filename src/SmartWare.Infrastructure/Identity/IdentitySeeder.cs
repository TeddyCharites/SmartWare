using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartWare.Domain.Constants;
using SmartWare.Domain.Enums;

namespace SmartWare.Infrastructure.Identity;

public static class IdentitySeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("IdentitySeeder");

        foreach (var roleName in RoleNames.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Không thể tạo role '{roleName}': {JoinErrors(roleResult)}");
            }
        }

        if (!isDevelopment)
        {
            return;
        }

        var email = configuration["DevelopmentAdmin:Email"];
        var password = configuration["DevelopmentAdmin:Password"];
        var fullName = configuration["DevelopmentAdmin:FullName"] ?? "Quản trị viên hệ thống";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Chưa seed tài khoản Admin development. Hãy cấu hình " +
                "DevelopmentAdmin:Email và DevelopmentAdmin:Password bằng User Secrets.");
            return;
        }

        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                Status = UserStatus.Active,
                LockoutEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var createResult = await userManager.CreateAsync(admin, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Không thể tạo Admin development: {JoinErrors(createResult)}");
            }
        }

        if (!await userManager.IsInRoleAsync(admin, RoleNames.Admin))
        {
            var roleResult = await userManager.AddToRoleAsync(admin, RoleNames.Admin);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Không thể gán role Admin: {JoinErrors(roleResult)}");
            }
        }
    }

    private static string JoinErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(x => x.Description));
}
