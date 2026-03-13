using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Entities;

public class AuditLogEntry : EntityBase
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Details { get; set; }
}
