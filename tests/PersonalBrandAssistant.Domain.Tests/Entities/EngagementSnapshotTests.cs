using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class EngagementSnapshotTests
{
    [Fact]
    public void EngagementSnapshot_Impressions_IsNullable()
    {
        var snapshot = new EngagementSnapshot();
        Assert.Null(snapshot.Impressions);
    }

    [Fact]
    public void EngagementSnapshot_Clicks_IsNullable()
    {
        var snapshot = new EngagementSnapshot();
        Assert.Null(snapshot.Clicks);
    }

    [Fact]
    public void EngagementSnapshot_StoresAllEngagementFields()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new EngagementSnapshot
        {
            ContentPlatformStatusId = Guid.NewGuid(),
            Likes = 100,
            Comments = 25,
            Shares = 10,
            Impressions = 5000,
            Clicks = 200,
            FetchedAt = now,
        };

        Assert.Equal(100, snapshot.Likes);
        Assert.Equal(25, snapshot.Comments);
        Assert.Equal(10, snapshot.Shares);
        Assert.Equal(5000, snapshot.Impressions);
        Assert.Equal(200, snapshot.Clicks);
        Assert.Equal(now, snapshot.FetchedAt);
    }
}
