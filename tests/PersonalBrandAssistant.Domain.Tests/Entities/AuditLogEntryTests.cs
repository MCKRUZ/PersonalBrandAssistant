using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class AuditLogEntryTests
{
    [Fact]
    public void AuditLogEntry_WithRequiredFields_CreatesSuccessfully()
    {
        var entry = new AuditLogEntry
        {
            EntityType = "Content",
            EntityId = Guid.NewGuid(),
            Action = "StatusChanged",
            Timestamp = DateTimeOffset.UtcNow,
        };

        Assert.Equal("Content", entry.EntityType);
        Assert.Equal("StatusChanged", entry.Action);
        Assert.NotEqual(Guid.Empty, entry.Id);
    }

    [Fact]
    public void OldValue_And_NewValue_AcceptNull()
    {
        var entry = new AuditLogEntry
        {
            EntityType = "Content",
            EntityId = Guid.NewGuid(),
            Action = "Created",
            Timestamp = DateTimeOffset.UtcNow,
            OldValue = null,
            NewValue = null,
        };

        Assert.Null(entry.OldValue);
        Assert.Null(entry.NewValue);
    }
}
