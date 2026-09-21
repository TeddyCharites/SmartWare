using SmartWare.Domain.Constants;

namespace SmartWare.AuthorizationTests.Roles;

public sealed class RoleNamesTests
{
    [Fact]
    public void System_DefinesExactlyThreeRoles()
    {
        Assert.Equal(["Admin", "Manager", "Employee"], RoleNames.All);
    }
}
