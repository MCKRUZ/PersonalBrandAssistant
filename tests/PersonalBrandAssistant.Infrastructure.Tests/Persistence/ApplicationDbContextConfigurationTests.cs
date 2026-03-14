using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;

namespace PersonalBrandAssistant.Infrastructure.Tests.Persistence;

public class ApplicationDbContextConfigurationTests
{
    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=fake")
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public void Content_HasQueryFilter()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(Content))!;
        var queryFilters = entityType.GetDeclaredQueryFilters();

        Assert.NotEmpty(queryFilters);
    }

    [Fact]
    public void Content_HasConcurrencyToken()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(Content))!;
        var xmin = entityType.FindProperty("xmin");

        Assert.NotNull(xmin);
        Assert.True(xmin!.IsConcurrencyToken);
    }

    [Fact]
    public void Platform_Type_HasUniqueIndex()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(Platform))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Platform.Type)));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    [Fact]
    public void ContentCalendarSlot_HasCompositeIndex()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(ContentCalendarSlot))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 2 &&
                i.Properties.Any(p => p.Name == nameof(ContentCalendarSlot.ScheduledDate)) &&
                i.Properties.Any(p => p.Name == nameof(ContentCalendarSlot.TargetPlatform)));

        Assert.NotNull(index);
    }

    [Fact]
    public void AuditLogEntry_Timestamp_HasIndex()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(AuditLogEntry))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(AuditLogEntry.Timestamp)));

        Assert.NotNull(index);
    }

    [Fact]
    public void User_Email_HasUniqueIndex()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(User))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(User.Email)));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    [Fact]
    public void Content_Status_HasIndex()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(Content))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Status"));

        Assert.NotNull(index);
    }

    [Fact]
    public void AgentExecution_HasCompositeIndexOnStatusAndAgentType()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(AgentExecution))!;
        var compositeIndex = entityType.GetIndexes().FirstOrDefault(i =>
            i.Properties.Any(p => p.Name == "Status") &&
            i.Properties.Any(p => p.Name == "AgentType"));

        Assert.NotNull(compositeIndex);
    }

    [Fact]
    public void AgentExecution_HasIndexOnContentId()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(AgentExecution))!;
        var index = entityType.GetIndexes().FirstOrDefault(i =>
            i.Properties.Any(p => p.Name == "ContentId"));

        Assert.NotNull(index);
    }

    [Fact]
    public void AgentExecutionLog_HasIndexOnAgentExecutionId()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(AgentExecutionLog))!;
        var index = entityType.GetIndexes().FirstOrDefault(i =>
            i.Properties.Any(p => p.Name == "AgentExecutionId"));

        Assert.NotNull(index);
    }

    [Fact]
    public void DbContext_IncludesAgentExecutionsDbSet()
    {
        using var context = CreateInMemoryContext();
        Assert.NotNull(context.Model.FindEntityType(typeof(AgentExecution)));
    }

    [Fact]
    public void DbContext_IncludesAgentExecutionLogsDbSet()
    {
        using var context = CreateInMemoryContext();
        Assert.NotNull(context.Model.FindEntityType(typeof(AgentExecutionLog)));
    }

    [Fact]
    public void AgentExecution_HasOptionalFkToContent_WithSetNullDeleteBehavior()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(AgentExecution))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "ContentId"));

        Assert.NotNull(fk);
        Assert.Equal(DeleteBehavior.SetNull, fk!.DeleteBehavior);
    }

    [Fact]
    public void AgentExecutionLog_HasRequiredFkToAgentExecution_WithCascadeDeleteBehavior()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(AgentExecutionLog))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "AgentExecutionId"));

        Assert.NotNull(fk);
        Assert.Equal(DeleteBehavior.Cascade, fk!.DeleteBehavior);
    }

    [Fact]
    public void AgentExecution_CostHasPrecision18Scale6()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(AgentExecution))!;
        var costProperty = entityType.FindProperty("Cost")!;

        Assert.Equal(18, costProperty.GetPrecision());
        Assert.Equal(6, costProperty.GetScale());
    }
}
