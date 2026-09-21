using System.Text.Json;
using SmartWare.Domain.Entities;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.Auditing;

internal static class AuditTrail
{
    public static void Add(
        ApplicationDbContext dbContext,
        string userId,
        string action,
        string module,
        string entityType,
        string entityId,
        object? oldValues,
        object? newValues)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            Module = module,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
