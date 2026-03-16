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
    public void CalendarSlot_HasCompositeIndexOnScheduledAtAndPlatform()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(CalendarSlot))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 2 &&
                i.Properties.Any(p => p.Name == nameof(CalendarSlot.ScheduledAt)) &&
                i.Properties.Any(p => p.Name == nameof(CalendarSlot.Platform)));

        Assert.NotNull(index);
    }

    [Fact]
    public void CalendarSlot_HasStatusIndex()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(CalendarSlot))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(CalendarSlot.Status)));

        Assert.NotNull(index);
    }

    [Fact]
    public void CalendarSlot_HasFkToContent_WithSetNullDelete()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(CalendarSlot))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "ContentId"));

        Assert.NotNull(fk);
        Assert.Equal(DeleteBehavior.SetNull, fk!.DeleteBehavior);
    }

    [Fact]
    public void CalendarSlot_HasFkToContentSeries_WithSetNullDelete()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(CalendarSlot))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "ContentSeriesId"));

        Assert.NotNull(fk);
        Assert.Equal(DeleteBehavior.SetNull, fk!.DeleteBehavior);
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

    [Fact]
    public void ContentPlatformStatus_IsRegistered()
    {
        using var context = CreateInMemoryContext();
        Assert.NotNull(context.Model.FindEntityType(typeof(ContentPlatformStatus)));
    }

    [Fact]
    public void ContentPlatformStatus_HasCompositeIndexOnContentIdAndPlatform()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(ContentPlatformStatus))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 2 &&
                i.Properties.Any(p => p.Name == "ContentId") &&
                i.Properties.Any(p => p.Name == "Platform"));

        Assert.NotNull(index);
    }

    [Fact]
    public void ContentPlatformStatus_HasUniqueIndexOnIdempotencyKey()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(ContentPlatformStatus))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "IdempotencyKey"));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    [Fact]
    public void ContentPlatformStatus_HasXminConcurrencyToken()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(ContentPlatformStatus))!;
        var xmin = entityType.FindProperty("xmin");

        Assert.NotNull(xmin);
        Assert.True(xmin!.IsConcurrencyToken);
    }

    [Fact]
    public void ContentPlatformStatus_HasFkToContent_WithCascadeDelete()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(ContentPlatformStatus))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "ContentId"));

        Assert.NotNull(fk);
        Assert.Equal(DeleteBehavior.Cascade, fk!.DeleteBehavior);
    }

    [Fact]
    public void OAuthState_IsRegistered()
    {
        using var context = CreateInMemoryContext();
        Assert.NotNull(context.Model.FindEntityType(typeof(OAuthState)));
    }

    [Fact]
    public void OAuthState_HasUniqueIndexOnState()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(OAuthState))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "State"));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    [Fact]
    public void OAuthState_HasIndexOnExpiresAt()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(OAuthState))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "ExpiresAt"));

        Assert.NotNull(index);
    }

    [Fact]
    public void Platform_GrantedScopes_HasTextArrayColumnType()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(Platform))!;
        var prop = entityType.FindProperty("GrantedScopes");

        Assert.NotNull(prop);
        Assert.Equal("text[]", prop!.GetColumnType());
    }

    [Fact]
    public void ContentSeries_IsRegistered()
    {
        using var context = CreateInMemoryContext();
        Assert.NotNull(context.Model.FindEntityType(typeof(ContentSeries)));
    }

    [Fact]
    public void ContentSeries_TargetPlatforms_HasIntegerArrayColumnType()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(ContentSeries))!;
        var prop = entityType.FindProperty("TargetPlatforms");

        Assert.NotNull(prop);
        Assert.Equal("integer[]", prop!.GetColumnType());
    }

    [Fact]
    public void ContentSeries_ThemeTags_HasJsonbColumnType()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(ContentSeries))!;
        var prop = entityType.FindProperty("ThemeTags");

        Assert.NotNull(prop);
        Assert.Equal("jsonb", prop!.GetColumnType());
    }

    [Fact]
    public void ContentSeries_HasIsActiveIndex()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(ContentSeries))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "IsActive"));

        Assert.NotNull(index);
    }

    [Fact]
    public void TrendSource_IsRegistered()
    {
        using var context = CreateInMemoryContext();
        Assert.NotNull(context.Model.FindEntityType(typeof(TrendSource)));
    }

    [Fact]
    public void TrendSource_HasUniqueIndexOnNameAndType()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(TrendSource))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 2 &&
                i.Properties.Any(p => p.Name == "Name") &&
                i.Properties.Any(p => p.Name == "Type"));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    [Fact]
    public void TrendItem_HasUniqueIndexOnDeduplicationKey()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(TrendItem))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "DeduplicationKey"));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    [Fact]
    public void TrendItem_HasDetectedAtIndex()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(TrendItem))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "DetectedAt"));

        Assert.NotNull(index);
    }

    [Fact]
    public void TrendItem_HasFkToTrendSource_WithSetNullDelete()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(TrendItem))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "TrendSourceId"));

        Assert.NotNull(fk);
        Assert.Equal(DeleteBehavior.SetNull, fk!.DeleteBehavior);
    }

    [Fact]
    public void TrendSuggestion_SuggestedPlatforms_HasIntegerArrayColumnType()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(TrendSuggestion))!;
        var prop = entityType.FindProperty("SuggestedPlatforms");

        Assert.NotNull(prop);
        Assert.Equal("integer[]", prop!.GetColumnType());
    }

    [Fact]
    public void TrendSuggestion_HasStatusIndex()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(TrendSuggestion))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Status"));

        Assert.NotNull(index);
    }

    [Fact]
    public void TrendSuggestionItem_HasCompositeKey()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(TrendSuggestionItem))!;
        var pk = entityType.FindPrimaryKey()!;

        Assert.Equal(2, pk.Properties.Count);
        Assert.Contains(pk.Properties, p => p.Name == "TrendSuggestionId");
        Assert.Contains(pk.Properties, p => p.Name == "TrendItemId");
    }

    [Fact]
    public void EngagementSnapshot_HasCompositeIndex()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(EngagementSnapshot))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 2 &&
                i.Properties.Any(p => p.Name == "ContentPlatformStatusId") &&
                i.Properties.Any(p => p.Name == "FetchedAt"));

        Assert.NotNull(index);
    }

    [Fact]
    public void EngagementSnapshot_HasFkToContentPlatformStatus_WithCascadeDelete()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(EngagementSnapshot))!;
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "ContentPlatformStatusId"));

        Assert.NotNull(fk);
        Assert.Equal(DeleteBehavior.Cascade, fk!.DeleteBehavior);
    }

    [Fact]
    public void Content_HasTreeDepthColumn()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(Content))!;
        var prop = entityType.FindProperty("TreeDepth");

        Assert.NotNull(prop);
    }

    [Fact]
    public void Content_HasRepurposeSourcePlatformColumn()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(Content))!;
        var prop = entityType.FindProperty("RepurposeSourcePlatform");

        Assert.NotNull(prop);
        Assert.True(prop!.IsNullable);
    }

    [Fact]
    public void Content_HasRepurposingUniqueConstraint()
    {
        using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(Content))!;
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 3 &&
                i.Properties.Any(p => p.Name == "ParentContentId") &&
                i.Properties.Any(p => p.Name == "RepurposeSourcePlatform") &&
                i.Properties.Any(p => p.Name == "ContentType"));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

}
