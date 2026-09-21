namespace SmartWare.Domain.Entities;

public sealed class AuditLog
{
    public long AuditLogId { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
