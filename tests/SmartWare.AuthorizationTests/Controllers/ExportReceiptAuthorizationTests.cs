using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using SmartWare.Domain.Constants;
using SmartWare.Web.Controllers;

namespace SmartWare.AuthorizationTests.Controllers;

public sealed class ExportReceiptAuthorizationTests
{
    [Fact]
    public void Controller_RequiresAuthentication()
    {
        var attribute = Assert.Single(
            typeof(ExportReceiptsController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Null(attribute.Policy);
        Assert.Null(attribute.Roles);
    }

    [Theory]
    [InlineData(nameof(ExportReceiptsController.Create), AuthorizationPolicies.CreateReceipts)]
    [InlineData(nameof(ExportReceiptsController.Approve), AuthorizationPolicies.ApproveReceipts)]
    [InlineData(nameof(ExportReceiptsController.Reject), AuthorizationPolicies.ApproveReceipts)]
    [InlineData(nameof(ExportReceiptsController.Complete), AuthorizationPolicies.CompleteReceipts)]
    public void MutationActions_RequireExpectedPolicy(string actionName, string policy)
    {
        var actions = typeof(ExportReceiptsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == actionName)
            .ToArray();

        Assert.NotEmpty(actions);
        Assert.All(actions, action =>
        {
            var attribute = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>());
            Assert.Equal(policy, attribute.Policy);
        });
    }
}
