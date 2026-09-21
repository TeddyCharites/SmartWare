using SmartWare.Domain.Enums;

namespace SmartWare.Application.Identity;

public sealed record UserSummary(
    string Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    UserStatus Status,
    string Role);
