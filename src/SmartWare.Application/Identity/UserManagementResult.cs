namespace SmartWare.Application.Identity;

public sealed record UserManagementResult(bool Succeeded, IReadOnlyCollection<string> Errors)
{
    public static UserManagementResult Success() => new(true, []);

    public static UserManagementResult Failure(IEnumerable<string> errors) =>
        new(false, errors.Distinct().ToArray());

    public static UserManagementResult Failure(string error) =>
        new(false, [error]);
}
