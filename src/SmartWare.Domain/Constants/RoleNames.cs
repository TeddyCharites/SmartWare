namespace SmartWare.Domain.Constants;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Employee = "Employee";

    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly([Admin, Manager, Employee]);
}
