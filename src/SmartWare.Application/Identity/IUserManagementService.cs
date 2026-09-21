namespace SmartWare.Application.Identity;

public interface IUserManagementService
{
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<UserSummary?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<UserManagementResult> CreateAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default);
    Task<UserManagementResult> UpdateAsync(
        UpdateUserCommand command,
        CancellationToken cancellationToken = default);
}
