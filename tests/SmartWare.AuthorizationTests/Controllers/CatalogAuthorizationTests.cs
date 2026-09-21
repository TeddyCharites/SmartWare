using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using SmartWare.Domain.Constants;
using SmartWare.Web.Controllers;

namespace SmartWare.AuthorizationTests.Controllers;

public sealed class CatalogAuthorizationTests
{
    [Theory]
    [InlineData(typeof(CategoriesController))]
    [InlineData(typeof(SuppliersController))]
    [InlineData(typeof(CustomersController))]
    public void MasterDataController_RequiresAdminPolicy(Type controllerType)
    {
        var attribute = Assert.Single(controllerType.GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.AdminOnly, attribute.Policy);
    }

    [Fact]
    public void OrdersController_RequiresAdminPolicy()
    {
        var attribute = Assert.Single(typeof(OrdersController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.AdminOnly, attribute.Policy);
    }

    [Fact]
    public void ReportsController_RequiresAdvancedReportsPolicy()
    {
        var attribute = Assert.Single(typeof(ReportsController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.AdvancedReports, attribute.Policy);
    }

    [Fact]
    public void KnowledgeController_AllowsOnlyAdminAndManager()
    {
        var attribute = Assert.Single(
            typeof(KnowledgeController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal("Admin,Manager", attribute.Roles);
    }

    [Fact]
    public void ProductsController_RequiresAuthentication()
    {
        var attribute = Assert.Single(typeof(ProductsController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Null(attribute.Policy);
        Assert.Null(attribute.Roles);
    }

    [Theory]
    [InlineData(nameof(ProductsController.Create))]
    [InlineData(nameof(ProductsController.Edit))]
    [InlineData(nameof(ProductsController.Delete))]
    public void ProductMutationActions_RequireAdminPolicy(string actionName)
    {
        var actions = typeof(ProductsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == actionName)
            .ToArray();

        Assert.NotEmpty(actions);
        Assert.All(actions, action =>
        {
            var attribute = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>());
            Assert.Equal(AuthorizationPolicies.AdminOnly, attribute.Policy);
        });
    }
}
