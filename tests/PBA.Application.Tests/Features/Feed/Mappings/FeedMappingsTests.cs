using PBA.Application.Features.Feed.Mappings;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Mappings;

public class FeedMappingsTests
{
    [Fact]
    public void ToDto_AllFieldsPopulated_MapsCorrectly()
    {
        var id = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var expires = created.AddDays(7);

        var entity = new FeedItem
        {
            Id = id,
            Type = FeedItemType.AgentDraft,
            Title = "New blog draft",
            Summary = "AI generated draft about cloud architecture",
            Data = """{"contentType":"Blog","primaryPlatform":"Substack","wordCount":1200}""",
            ActionType = "approve",
            ActionTargetId = targetId,
            Priority = FeedItemPriority.High,
            IsRead = true,
            IsActedOn = false,
            CreatedAt = created,
            ExpiresAt = expires,
        };

        var dto = entity.ToDto();

        Assert.Equal(id, dto.Id);
        Assert.Equal(FeedItemType.AgentDraft, dto.Type);
        Assert.Equal("New blog draft", dto.Title);
        Assert.Equal("AI generated draft about cloud architecture", dto.Summary);
        Assert.Equal("""{"contentType":"Blog","primaryPlatform":"Substack","wordCount":1200}""", dto.Data);
        Assert.Equal("approve", dto.ActionType);
        Assert.Equal(targetId, dto.ActionTargetId);
        Assert.Equal(FeedItemPriority.High, dto.Priority);
        Assert.True(dto.IsRead);
        Assert.False(dto.IsActedOn);
        Assert.Equal(created, dto.CreatedAt);
        Assert.Equal(expires, dto.ExpiresAt);
    }

    [Fact]
    public void ToDto_NullOptionalFields_HandlesGracefully()
    {
        var entity = new FeedItem
        {
            Title = "System notification",
            Type = FeedItemType.SystemNotification,
            Data = null,
            ActionType = null,
            ActionTargetId = null,
            ExpiresAt = null,
        };

        var dto = entity.ToDto();

        Assert.Null(dto.Data);
        Assert.Null(dto.ActionType);
        Assert.Null(dto.ActionTargetId);
        Assert.Null(dto.ExpiresAt);
    }
}
