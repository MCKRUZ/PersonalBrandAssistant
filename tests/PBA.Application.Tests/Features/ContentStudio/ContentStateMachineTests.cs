using PBA.Application.Features.ContentStudio;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Application.Tests.Features.ContentStudio;

public class ContentStateMachineTests
{
    private static Domain.Entities.Content CreateContent(
        ContentStatus status = ContentStatus.Idea,
        string body = "",
        DateTimeOffset? scheduledAt = null)
    {
        return new Domain.Entities.Content
        {
            Title = "Test",
            Status = status,
            Body = body,
            ScheduledAt = scheduledAt
        };
    }

    [Fact]
    public async Task Fire_StartDraft_FromIdea_TransitionsToDraft()
    {
        var content = CreateContent(ContentStatus.Idea);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.StartDraft);

        Assert.Equal(ContentStatus.Draft, content.Status);
    }

    [Fact]
    public async Task Fire_SubmitForReview_FromDraft_TransitionsToReview()
    {
        var content = CreateContent(ContentStatus.Draft, body: "some content");
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.SubmitForReview);

        Assert.Equal(ContentStatus.Review, content.Status);
    }

    [Fact]
    public async Task Fire_Approve_FromDraft_TransitionsToApproved()
    {
        var content = CreateContent(ContentStatus.Draft, body: "some content");
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Approve);

        Assert.Equal(ContentStatus.Approved, content.Status);
    }

    [Fact]
    public async Task Fire_Archive_FromDraft_TransitionsToArchived()
    {
        var content = CreateContent(ContentStatus.Draft);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Archive);

        Assert.Equal(ContentStatus.Archived, content.Status);
    }

    [Fact]
    public async Task Fire_Approve_FromReview_TransitionsToApproved()
    {
        var content = CreateContent(ContentStatus.Review);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Approve);

        Assert.Equal(ContentStatus.Approved, content.Status);
    }

    [Fact]
    public async Task Fire_RequestChanges_FromReview_TransitionsToDraft()
    {
        var content = CreateContent(ContentStatus.Review);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.RequestChanges);

        Assert.Equal(ContentStatus.Draft, content.Status);
    }

    [Fact]
    public async Task Fire_Archive_FromReview_TransitionsToArchived()
    {
        var content = CreateContent(ContentStatus.Review);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Archive);

        Assert.Equal(ContentStatus.Archived, content.Status);
    }

    [Fact]
    public async Task Fire_Schedule_FromApproved_TransitionsToScheduled()
    {
        var content = CreateContent(ContentStatus.Approved, scheduledAt: DateTimeOffset.UtcNow.AddDays(1));
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Schedule);

        Assert.Equal(ContentStatus.Scheduled, content.Status);
    }

    [Fact]
    public async Task Fire_PublishNow_FromApproved_TransitionsToPublished()
    {
        var content = CreateContent(ContentStatus.Approved);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.PublishNow);

        Assert.Equal(ContentStatus.Published, content.Status);
    }

    [Fact]
    public async Task Fire_Publish_FromScheduled_TransitionsToPublished()
    {
        var content = CreateContent(ContentStatus.Scheduled);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Publish);

        Assert.Equal(ContentStatus.Published, content.Status);
    }

    [Fact]
    public async Task Fire_Unschedule_FromScheduled_TransitionsToApproved()
    {
        var content = CreateContent(ContentStatus.Scheduled);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Unschedule);

        Assert.Equal(ContentStatus.Approved, content.Status);
    }

    [Fact]
    public async Task Fire_Archive_FromPublished_TransitionsToArchived()
    {
        var content = CreateContent(ContentStatus.Published);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Archive);

        Assert.Equal(ContentStatus.Archived, content.Status);
    }

    [Fact]
    public async Task Fire_Unpublish_FromPublished_TransitionsToDraft()
    {
        var content = CreateContent(ContentStatus.Published);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Unpublish);

        Assert.Equal(ContentStatus.Draft, content.Status);
    }

    [Fact]
    public async Task Fire_Restore_FromArchived_TransitionsToDraft()
    {
        var content = CreateContent(ContentStatus.Archived);
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Restore);

        Assert.Equal(ContentStatus.Draft, content.Status);
    }

    [Fact]
    public async Task Fire_SubmitForReview_FromDraft_FailsWhenBodyEmpty()
    {
        var content = CreateContent(ContentStatus.Draft, body: "");
        var machine = ContentStateMachine.Create(content);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => machine.FireAsync(ContentTrigger.SubmitForReview));
    }

    [Fact]
    public async Task Fire_Approve_FromDraft_FailsWhenBodyEmpty()
    {
        var content = CreateContent(ContentStatus.Draft, body: "");
        var machine = ContentStateMachine.Create(content);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => machine.FireAsync(ContentTrigger.Approve));
    }

    [Fact]
    public async Task Fire_Schedule_FromApproved_FailsWhenScheduledAtNull()
    {
        var content = CreateContent(ContentStatus.Approved, scheduledAt: null);
        var machine = ContentStateMachine.Create(content);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => machine.FireAsync(ContentTrigger.Schedule));
    }

    [Fact]
    public async Task Fire_Schedule_FromApproved_FailsWhenScheduledAtInPast()
    {
        var content = CreateContent(ContentStatus.Approved, scheduledAt: DateTimeOffset.UtcNow.AddDays(-1));
        var machine = ContentStateMachine.Create(content);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => machine.FireAsync(ContentTrigger.Schedule));
    }

    [Fact]
    public async Task Fire_InvalidTransition_IdeaToPublished_Throws()
    {
        var content = CreateContent(ContentStatus.Idea);
        var machine = ContentStateMachine.Create(content);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => machine.FireAsync(ContentTrigger.PublishNow));
    }

    [Fact]
    public async Task Fire_Publish_SetsPublishedAtAndUpdatedAt()
    {
        var content = CreateContent(ContentStatus.Approved);
        var beforeFire = content.UpdatedAt;
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.PublishNow);

        Assert.NotNull(content.PublishedAt);
        Assert.True(content.UpdatedAt >= beforeFire);
    }

    [Fact]
    public async Task Fire_Unpublish_ClearsScheduledAtAndHangfireJobId()
    {
        var content = CreateContent(ContentStatus.Published);
        content.ScheduledAt = DateTimeOffset.UtcNow.AddDays(-1);
        content.HangfireJobId = "job1";
        var machine = ContentStateMachine.Create(content);

        await machine.FireAsync(ContentTrigger.Unpublish);

        Assert.Null(content.ScheduledAt);
        Assert.Null(content.HangfireJobId);
        Assert.Equal(ContentStatus.Draft, content.Status);
    }
}
