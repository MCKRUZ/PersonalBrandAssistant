using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Enums;

public class EnumTests
{
    [Fact]
    public void ContentType_HasExactly4Values()
    {
        var values = Enum.GetValues<ContentType>();
        Assert.Equal(4, values.Length);
        Assert.Contains(ContentType.BlogPost, values);
        Assert.Contains(ContentType.SocialPost, values);
        Assert.Contains(ContentType.Thread, values);
        Assert.Contains(ContentType.VideoDescription, values);
    }

    [Fact]
    public void ContentStatus_HasExactly8Values()
    {
        var values = Enum.GetValues<ContentStatus>();
        Assert.Equal(8, values.Length);
    }

    [Fact]
    public void PlatformType_HasExactly4Values()
    {
        var values = Enum.GetValues<PlatformType>();
        Assert.Equal(4, values.Length);
        Assert.Contains(PlatformType.TwitterX, values);
        Assert.Contains(PlatformType.LinkedIn, values);
        Assert.Contains(PlatformType.Instagram, values);
        Assert.Contains(PlatformType.YouTube, values);
    }

    [Fact]
    public void AutonomyLevel_HasExactly4Values()
    {
        var values = Enum.GetValues<AutonomyLevel>();
        Assert.Equal(4, values.Length);
        Assert.Contains(AutonomyLevel.Manual, values);
        Assert.Contains(AutonomyLevel.Assisted, values);
        Assert.Contains(AutonomyLevel.SemiAuto, values);
        Assert.Contains(AutonomyLevel.Autonomous, values);
    }

    [Fact]
    public void NotificationType_HasExactly8Values()
    {
        var values = Enum.GetValues<NotificationType>();
        Assert.Equal(8, values.Length);
        Assert.Contains(NotificationType.ContentReadyForReview, values);
        Assert.Contains(NotificationType.ContentApproved, values);
        Assert.Contains(NotificationType.ContentRejected, values);
        Assert.Contains(NotificationType.ContentPublished, values);
        Assert.Contains(NotificationType.ContentFailed, values);
        Assert.Contains(NotificationType.PlatformDisconnected, values);
        Assert.Contains(NotificationType.PlatformTokenExpiring, values);
        Assert.Contains(NotificationType.PlatformScopeMismatch, values);
    }

    [Fact]
    public void ActorType_HasExactly3Values()
    {
        var values = Enum.GetValues<ActorType>();
        Assert.Equal(3, values.Length);
        Assert.Contains(ActorType.User, values);
        Assert.Contains(ActorType.System, values);
        Assert.Contains(ActorType.Agent, values);
    }

    [Fact]
    public void AgentCapabilityType_HasExactly5Values()
    {
        var values = Enum.GetValues<AgentCapabilityType>();
        Assert.Equal(5, values.Length);
        Assert.Contains(AgentCapabilityType.Writer, values);
        Assert.Contains(AgentCapabilityType.Social, values);
        Assert.Contains(AgentCapabilityType.Repurpose, values);
        Assert.Contains(AgentCapabilityType.Engagement, values);
        Assert.Contains(AgentCapabilityType.Analytics, values);
    }

    [Fact]
    public void AgentExecutionStatus_HasExactly5Values()
    {
        var values = Enum.GetValues<AgentExecutionStatus>();
        Assert.Equal(5, values.Length);
        Assert.Contains(AgentExecutionStatus.Pending, values);
        Assert.Contains(AgentExecutionStatus.Running, values);
        Assert.Contains(AgentExecutionStatus.Completed, values);
        Assert.Contains(AgentExecutionStatus.Failed, values);
        Assert.Contains(AgentExecutionStatus.Cancelled, values);
    }

    [Fact]
    public void ModelTier_HasExactly3Values()
    {
        var values = Enum.GetValues<ModelTier>();
        Assert.Equal(3, values.Length);
        Assert.Contains(ModelTier.Fast, values);
        Assert.Contains(ModelTier.Standard, values);
        Assert.Contains(ModelTier.Advanced, values);
    }

    [Fact]
    public void PlatformPublishStatus_HasExactly6Values()
    {
        var values = Enum.GetValues<PlatformPublishStatus>();
        Assert.Equal(6, values.Length);
        Assert.Contains(PlatformPublishStatus.Pending, values);
        Assert.Contains(PlatformPublishStatus.Published, values);
        Assert.Contains(PlatformPublishStatus.Failed, values);
        Assert.Contains(PlatformPublishStatus.RateLimited, values);
        Assert.Contains(PlatformPublishStatus.Skipped, values);
        Assert.Contains(PlatformPublishStatus.Processing, values);
    }

    [Fact]
    public void TrendSourceType_HasExactly4Values()
    {
        var values = Enum.GetValues<TrendSourceType>();
        Assert.Equal(4, values.Length);
        Assert.Contains(TrendSourceType.TrendRadar, values);
        Assert.Contains(TrendSourceType.FreshRSS, values);
        Assert.Contains(TrendSourceType.Reddit, values);
        Assert.Contains(TrendSourceType.HackerNews, values);
    }

    [Fact]
    public void TrendSuggestionStatus_HasExactly3Values()
    {
        var values = Enum.GetValues<TrendSuggestionStatus>();
        Assert.Equal(3, values.Length);
        Assert.Contains(TrendSuggestionStatus.Pending, values);
        Assert.Contains(TrendSuggestionStatus.Accepted, values);
        Assert.Contains(TrendSuggestionStatus.Dismissed, values);
    }

    [Fact]
    public void CalendarSlotStatus_HasExactly4Values()
    {
        var values = Enum.GetValues<CalendarSlotStatus>();
        Assert.Equal(4, values.Length);
        Assert.Contains(CalendarSlotStatus.Open, values);
        Assert.Contains(CalendarSlotStatus.Filled, values);
        Assert.Contains(CalendarSlotStatus.Published, values);
        Assert.Contains(CalendarSlotStatus.Skipped, values);
    }
}
