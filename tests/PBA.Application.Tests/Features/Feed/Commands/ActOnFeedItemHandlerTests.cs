using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Commands;
using PBA.Application.Features.Feed.Commands;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using static PBA.Application.Tests.Features.Feed.FeedTestHelpers;

namespace PBA.Application.Tests.Features.Feed.Commands;

public class ActOnFeedItemHandlerTests
{
    [Fact]
    public async Task Handle_AgentDraftApprove_DispatchesApproveContentAndMarksActedOn()
    {
        await using var context = CreateContext();
        var contentId = Guid.NewGuid();
        var item = CreateFeedItem(type: FeedItemType.AgentDraft, actionTargetId: contentId);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        senderMock.Setup(s => s.Send(It.IsAny<ApproveContent.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "approve"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        senderMock.Verify(
            s => s.Send(It.Is<ApproveContent.Command>(c => c.ContentId == contentId), It.IsAny<CancellationToken>()),
            Times.Once);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_AgentDraftDismiss_MarksReadAndActedOn()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.AgentDraft);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "dismiss"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_TrendAlertView_MarksAsRead()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.TrendAlert);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "view"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.False(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_TrendAlertDismiss_MarksReadAndActedOn()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.TrendAlert);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "dismiss"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_IdeaSuggestionCreateContent_DeserializesDataAndDispatchesCommand()
    {
        await using var context = CreateContext();
        var ideaId = Guid.NewGuid();
        var newContentId = Guid.NewGuid();
        var item = CreateFeedItem(
            type: FeedItemType.IdeaSuggestion,
            actionTargetId: ideaId,
            data: @"{""contentType"":""BlogPost"",""primaryPlatform"":""Blog"",""keywords"":[""AI""],""confidence"":0.85,""sourceIdeaTitle"":""Test""}");
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        senderMock.Setup(s => s.Send(It.IsAny<CreateContentFromIdea.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(newContentId));

        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "create-content"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"/content/{newContentId}", result.Value!.NavigationTarget);
        Assert.Equal(newContentId, result.Value.TargetId);
        senderMock.Verify(
            s => s.Send(
                It.Is<CreateContentFromIdea.Command>(c =>
                    c.IdeaId == ideaId &&
                    c.ContentType == ContentType.BlogPost &&
                    c.PrimaryPlatform == Platform.Blog),
                It.IsAny<CancellationToken>()),
            Times.Once);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_IdeaSuggestionDismiss_MarksReadAndActedOn()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.IdeaSuggestion);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "dismiss"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_AnalyticsHighlightView_MarksAsRead()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.AnalyticsHighlight);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "view"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.False(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_ApprovalRequestApprove_DispatchesApproveContentAndMarksActedOn()
    {
        await using var context = CreateContext();
        var contentId = Guid.NewGuid();
        var item = CreateFeedItem(type: FeedItemType.ApprovalRequest, actionTargetId: contentId);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        senderMock.Setup(s => s.Send(It.IsAny<ApproveContent.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "approve"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("/content", result.Value!.NavigationTarget);
        senderMock.Verify(
            s => s.Send(It.Is<ApproveContent.Command>(c => c.ContentId == contentId), It.IsAny<CancellationToken>()),
            Times.Once);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.True(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_SystemNotificationView_MarksAsRead()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.SystemNotification);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "view"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.True(updated!.IsRead);
        Assert.False(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_UnknownAction_ReturnsValidationFailure()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.SystemNotification);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "invalid-action"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.Validation, result.FailureType);
    }

    [Fact]
    public async Task Handle_NonexistentFeedItem_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(Guid.NewGuid(), "view"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }

    [Fact]
    public async Task Handle_SubCommandFails_DoesNotMarkActedOn()
    {
        await using var context = CreateContext();
        var targetId = Guid.NewGuid();
        var item = CreateFeedItem(type: FeedItemType.AgentDraft, actionTargetId: targetId);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        senderMock.Setup(s => s.Send(It.IsAny<ApproveContent.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail("Approval failed"));

        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "approve"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        var updated = await context.FeedItems.FindAsync(item.Id);
        Assert.False(updated!.IsRead);
        Assert.False(updated.IsActedOn);
    }

    [Fact]
    public async Task Handle_ApproveWithNullActionTargetId_ReturnsValidationFailure()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.AgentDraft, actionTargetId: null);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "approve"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.Validation, result.FailureType);
        senderMock.Verify(s => s.Send(It.IsAny<ApproveContent.Command>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CreateContentWithNullData_ReturnsValidationFailure()
    {
        await using var context = CreateContext();
        var targetId = Guid.NewGuid();
        var item = CreateFeedItem(type: FeedItemType.IdeaSuggestion, actionTargetId: targetId, data: null);
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "create-content"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.Validation, result.FailureType);
    }

    [Fact]
    public async Task Handle_CreateContentWithMalformedJson_ReturnsValidationFailure()
    {
        await using var context = CreateContext();
        var targetId = Guid.NewGuid();
        var item = CreateFeedItem(type: FeedItemType.IdeaSuggestion, actionTargetId: targetId, data: "not-json");
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "create-content"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.Validation, result.FailureType);
    }

    [Fact]
    public async Task Handle_CreateContentWithMissingContentType_ReturnsValidationFailure()
    {
        await using var context = CreateContext();
        var targetId = Guid.NewGuid();
        var item = CreateFeedItem(type: FeedItemType.IdeaSuggestion, actionTargetId: targetId,
            data: "{\"primaryPlatform\":\"Blog\"}");
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "create-content"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.Validation, result.FailureType);
    }

    [Fact]
    public async Task Handle_CreateContentWithNullActionTargetId_ReturnsValidationFailure()
    {
        await using var context = CreateContext();
        var item = CreateFeedItem(type: FeedItemType.IdeaSuggestion, actionTargetId: null,
            data: "{\"contentType\":\"BlogPost\",\"primaryPlatform\":\"Blog\"}");
        context.FeedItems.Add(item);
        await context.SaveChangesAsync();

        var senderMock = new Mock<ISender>();
        var handler = new ActOnFeedItem.Handler(context, senderMock.Object);
        var result = await handler.Handle(new ActOnFeedItem.Command(item.Id, "create-content"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.Validation, result.FailureType);
        senderMock.Verify(s => s.Send(It.IsAny<CreateContentFromIdea.Command>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
