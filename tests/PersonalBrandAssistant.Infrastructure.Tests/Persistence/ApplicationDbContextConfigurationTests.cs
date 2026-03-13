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
}
