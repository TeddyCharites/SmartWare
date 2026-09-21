using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartWare.Domain.Constants;
using SmartWare.Infrastructure;

namespace SmartWare.AuthorizationTests.Policies;

public sealed class AuthorizationPolicyTests
{
    public static TheoryData<string, string[]> Policies => new()
    {
        { AuthorizationPolicies.AdminOnly, [RoleNames.Admin] },
        { AuthorizationPolicies.ManagerOnly, [RoleNames.Manager] },
        { AuthorizationPolicies.CreateReceipts, [RoleNames.Admin, RoleNames.Employee] },
        { AuthorizationPolicies.ApproveReceipts, [RoleNames.Manager] },
        { AuthorizationPolicies.CompleteReceipts, [RoleNames.Admin, RoleNames.Employee] },
        { AuthorizationPolicies.AdvancedReports, [RoleNames.Admin, RoleNames.Manager] }
    };

    [Theory]
    [MemberData(nameof(Policies))]
    public async Task Policy_AllowsExactlyTheExpectedRoles(
        string policyName,
        string[] expectedRoles)
    {
        await using var provider = CreateServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(policyName);

        var roleRequirement = Assert.Single(policy!.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(expectedRoles, roleRequirement.AllowedRoles);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=(localdb)\\MSSQLLocalDB;Database=SmartWareAuthorizationTests;Trusted_Connection=True"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
