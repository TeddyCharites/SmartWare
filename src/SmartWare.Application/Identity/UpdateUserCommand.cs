using SmartWare.Domain.Enums;

namespace SmartWare.Application.Identity;

public sealed record UpdateUserCommand(
    string Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    UserStatus Status,
    string Role,
    string PerformedById);
