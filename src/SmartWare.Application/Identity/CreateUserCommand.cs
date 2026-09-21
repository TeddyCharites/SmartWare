namespace SmartWare.Application.Identity;

public sealed record CreateUserCommand(
    string FullName,
    string Email,
    string? PhoneNumber,
    string Password,
    string Role,
    string PerformedById);
