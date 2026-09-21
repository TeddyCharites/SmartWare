using Microsoft.AspNetCore.Authorization;
using SmartWare.Web.Controllers;

namespace SmartWare.AuthorizationTests.Controllers;

public sealed class InventoryAuthorizationTests
{
    [Fact]
    public void Controller_RequiresAuthenticatedUser()
    {
        var attribute = Assert.Single(
            typeof(InventoryController).GetCustomAttributes(typeof(AuthorizeAttribute), true));

        var authorize = Assert.IsType<AuthorizeAttribute>(attribute);
        Assert.Null(authorize.Policy);
        Assert.Null(authorize.Roles);
    }
}
