using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.Events;

namespace PersonalBrandAssistant.Domain.Tests.Events;

public class DomainEventTests
{
    [Fact]
    public void ContentApprovedEvent_ContainsCorrectContentId()
    {
        var contentId = Guid.NewGuid();
        var evt = new ContentApprovedEvent(contentId);

        Assert.Equal(contentId, evt.ContentId);
    }

    [Fact]
    public void ContentRejectedEvent_ContainsContentIdAndFeedback()
    {
        var contentId = Guid.NewGuid();
        var evt = new ContentRejectedEvent(contentId, "Needs revision");

        Assert.Equal(contentId, evt.ContentId);
        Assert.Equal("Needs revision", evt.Feedback);
    }

    [Fact]
    public void ContentScheduledEvent_ContainsContentIdAndScheduledAt()
    {
        var contentId = Guid.NewGuid();
        var scheduledAt = DateTimeOffset.UtcNow.AddHours(1);
        var evt = new ContentScheduledEvent(contentId, scheduledAt);

        Assert.Equal(contentId, evt.ContentId);
        Assert.Equal(scheduledAt, evt.ScheduledAt);
    }

    [Fact]
    public void ContentPublishedEvent_ContainsContentIdAndPlatforms()
    {
        var contentId = Guid.NewGuid();
        var platforms = new[] { PlatformType.TwitterX, PlatformType.LinkedIn };
        var evt = new ContentPublishedEvent(contentId, platforms);

        Assert.Equal(contentId, evt.ContentId);
        Assert.Equal(platforms, evt.Platforms);
    }

    [Fact]
    public void AgentExecutionCompletedEvent_ContainsExecutionIdAndContentId()
    {
        var executionId = Guid.NewGuid();
        var contentId = Guid.NewGuid();
        var evt = new AgentExecutionCompletedEvent(executionId, contentId);

        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal(contentId, evt.ContentId);
    }

    [Fact]
    public void AgentExecutionCompletedEvent_WithNullContentId_IsValid()
    {
        var executionId = Guid.NewGuid();
        var evt = new AgentExecutionCompletedEvent(executionId, null);

        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Null(evt.ContentId);
    }

    [Fact]
    public void AgentExecutionFailedEvent_ContainsExecutionIdAndError()
    {
        var executionId = Guid.NewGuid();
        var evt = new AgentExecutionFailedEvent(executionId, "Budget exceeded");

        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal("Budget exceeded", evt.Error);
    }

    [Fact]
    public void AllEventTypes_ImplementIDomainEvent()
    {
        Assert.IsAssignableFrom<IDomainEvent>(new ContentApprovedEvent(Guid.NewGuid()));
        Assert.IsAssignableFrom<IDomainEvent>(new ContentRejectedEvent(Guid.NewGuid(), "feedback"));
        Assert.IsAssignableFrom<IDomainEvent>(new ContentScheduledEvent(Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.IsAssignableFrom<IDomainEvent>(new ContentPublishedEvent(Guid.NewGuid(), [PlatformType.TwitterX]));
        Assert.IsAssignableFrom<IDomainEvent>(new AgentExecutionCompletedEvent(Guid.NewGuid(), null));
        Assert.IsAssignableFrom<IDomainEvent>(new AgentExecutionFailedEvent(Guid.NewGuid(), "error"));
    }
}
