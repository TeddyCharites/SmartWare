namespace SmartWare.Application.Common;

public sealed record OperationResult(
    bool Succeeded,
    string? Message,
    IReadOnlyCollection<string> Errors)
{
    public static OperationResult Success(string message) => new(true, message, []);

    public static OperationResult Failure(string error) => new(false, null, [error]);

    public static OperationResult Failure(IEnumerable<string> errors) =>
        new(false, null, errors.Distinct().ToArray());
}
