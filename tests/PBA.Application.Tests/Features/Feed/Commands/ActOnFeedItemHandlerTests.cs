using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using PBA.Application.Features.Content.Commands;
using PBA.Application.Features.Feed.Commands;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class ActOnFeedItemHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_AgentDraft_Approve_DispatchesApproveContentAndMarksActedOn()
    {
        await using var context = CreateContext();
        var contentId = Guid.NewGuid();
        var item = new FeedItem
        {
            Title = "Draft ready",
            Type = FeedItemType.AgentDraft,
            ActionTargetId = contentId
        };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ApproveContent.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "approve"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/content", result.Value!.NavigationTarget);
        Assert.Equal(contentId, result.Value.TargetId);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
        sender.Verify(s => s.Send(It.Is<ApproveContent.Command>(c => c.ContentId == contentId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AgentDraft_Dismiss_MarksReadAndActedOn()
    {
        await using var context = CreateContext();
        var item = new FeedItem { Title = "Draft", Type = FeedItemType.AgentDraft };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var sender = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "dismiss"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_TrendAlert_View_MarksRead()
    {
        await using var context = CreateContext();
        var item = new FeedItem { Title = "Trend", Type = FeedItemType.TrendAlert };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var sender = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "view"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.False(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_TrendAlert_Dismiss_MarksReadAndActedOn()
    {
        await using var context = CreateContext();
        var item = new FeedItem { Title = "Trend", Type = FeedItemType.TrendAlert };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var sender = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "dismiss"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_IdeaSuggestion_CreateContent_DispatchesCreateContentFromIdea()
    {
        await using var context = CreateContext();
        var ideaId = Guid.NewGuid();
        var newContentId = Guid.NewGuid();
        var item = new FeedItem
        {
            Title = "Idea",
            Type = FeedItemType.IdeaSuggestion,
            ActionTargetId = ideaId,
            Data = @"{""contentType"":""BlogPost"",""primaryPlatform"":""LinkedIn""}"
        };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<CreateContentFromIdea.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(newContentId));

        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "create-content"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/content/{newContentId}", result.Value!.NavigationTarget);
        Assert.Equal(newContentId, result.Value.TargetId);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_IdeaSuggestion_Dismiss_MarksReadAndActedOn()
    {
        await using var context = CreateContext();
        var item = new FeedItem { Title = "Idea", Type = FeedItemType.IdeaSuggestion };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var sender = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "dismiss"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_ApprovalRequest_Approve_DispatchesApproveContent()
    {
        await using var context = CreateContext();
        var contentId = Guid.NewGuid();
        var item = new FeedItem
        {
            Title = "Approval",
            Type = FeedItemType.ApprovalRequest,
            ActionTargetId = contentId
        };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ApproveContent.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "approve"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/content", result.Value!.NavigationTarget);
    }

    [Fact]
    public async Task Handle_SystemNotification_View_MarksRead()
    {
        await using var context = CreateContext();
        var item = new FeedItem { Title = "Notif", Type = FeedItemType.SystemNotification };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var sender = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "view"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
    }

    [Fact]
    public async Task Handle_UnknownAction_ReturnsValidationFailure()
    {
        await using var context = CreateContext();
        var item = new FeedItem { Title = "Test", Type = FeedItemType.SystemNotification };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var sender = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "nonexistent"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.Validation, result.FailureType);
    }

    [Fact]
    public async Task Handle_NonexistentItem_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var sender = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(Guid.NewGuid(), "view"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }

    [Fact]
    public async Task Handle_SubCommandFails_DoesNotMarkActedOn()
    {
        await using var context = CreateContext();
        var item = new FeedItem
        {
            Title = "Draft",
            Type = FeedItemType.AgentDraft,
            ActionTargetId = Guid.NewGuid()
        };
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ApproveContent.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.NotFound("Content not found"));

        var handler = new ActOnFeedItem.Handler(context, sender.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "approve"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.False(updated!.IsRead);
        Assert.False(updated.IsActedOn);
    }
}
