using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Tests.Features.Approval;

public class ApprovalServiceTests
{
    private readonly Mock<IWorkflowEngine> _workflowEngine = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<ILogger<ApprovalService>> _logger = new();
    private readonly ApprovalService _service;

    public ApprovalServiceTests()
    {
        _service = new ApprovalService(
            _workflowEngine.Object, _notificationService.Object,
            _dbContext.Object, _logger.Object);
    }

    private void SetupContents(params ContentEntity[] contents)
    {
        var mockDbSet = contents.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
    }

    private static ContentEntity CreateInStatus(ContentStatus status)
    {
        var content = ContentEntity.Create(ContentType.BlogPost, "Test body");
        TransitionTo(content, status);
        return content;
    }

    private static void TransitionTo(ContentEntity content, ContentStatus target)
    {
        if (content.Status == target) return;
        var path = target switch
        {
            ContentStatus.Review => new[] { ContentStatus.Review },
            ContentStatus.Approved => new[] { ContentStatus.Review, ContentStatus.Approved },
            ContentStatus.Scheduled => new[] { ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled },
            _ => Array.Empty<ContentStatus>()
        };
        foreach (var s in path) content.TransitionTo(s);
    }

    [Fact]
    public async Task ApproveAsync_WhenContentInReview_TransitionsToApproved()
    {
        var content = CreateInStatus(ContentStatus.Review);
        SetupContents(content);
        _workflowEngine.Setup(x => x.TransitionAsync(
                content.Id, ContentStatus.Approved, null, ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var result = await _service.ApproveAsync(content.Id);

        Assert.True(result.IsSuccess);
        _workflowEngine.Verify(x => x.TransitionAsync(
            content.Id, ContentStatus.Approved, null, ActorType.User, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_WhenContentHasScheduledAt_ChainsToScheduled()
    {
        var content = CreateInStatus(ContentStatus.Review);
        content.ScheduledAt = DateTimeOffset.UtcNow.AddHours(1);
        SetupContents(content);
        _workflowEngine.Setup(x => x.TransitionAsync(
                content.Id, ContentStatus.Approved, null, ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
        _workflowEngine.Setup(x => x.TransitionAsync(
                content.Id, ContentStatus.Scheduled, null, ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var result = await _service.ApproveAsync(content.Id);

        Assert.True(result.IsSuccess);
        _workflowEngine.Verify(x => x.TransitionAsync(
            content.Id, ContentStatus.Scheduled, null, ActorType.User, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_WhenContentNotInReview_ReturnsFailure()
    {
        var content = CreateInStatus(ContentStatus.Draft);
        SetupContents(content);
        _workflowEngine.Setup(x => x.TransitionAsync(
                content.Id, ContentStatus.Approved, null, ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed, "Invalid transition"));

        var result = await _service.ApproveAsync(content.Id);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RejectAsync_TransitionsReviewToDraft_WithFeedback()
    {
        var content = CreateInStatus(ContentStatus.Review);
        SetupContents(content);
        _workflowEngine.Setup(x => x.TransitionAsync(
                content.Id, ContentStatus.Draft, "Needs more detail", ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var result = await _service.RejectAsync(content.Id, "Needs more detail");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RejectAsync_SendsContentRejectedNotification()
    {
        var content = CreateInStatus(ContentStatus.Review);
        SetupContents(content);
        _workflowEngine.Setup(x => x.TransitionAsync(
                It.IsAny<Guid>(), ContentStatus.Draft, It.IsAny<string?>(), It.IsAny<ActorType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        await _service.RejectAsync(content.Id, "Needs revision");

        _notificationService.Verify(x => x.SendAsync(
            NotificationType.ContentRejected,
            It.Is<string>(t => t.Contains(content.Id.ToString())),
            "Needs revision",
            content.Id,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BatchApproveAsync_ApprovesMultipleItems_ReturnsSuccessCount()
    {
        var c1 = CreateInStatus(ContentStatus.Review);
        var c2 = CreateInStatus(ContentStatus.Review);
        var c3 = CreateInStatus(ContentStatus.Review);
        SetupContents(c1, c2, c3);
        _workflowEngine.Setup(x => x.TransitionAsync(
                It.IsAny<Guid>(), ContentStatus.Approved, null, ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var result = await _service.BatchApproveAsync([c1.Id, c2.Id, c3.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
    }

    [Fact]
    public async Task BatchApproveAsync_HandlesPartialFailures()
    {
        var c1 = CreateInStatus(ContentStatus.Review);
        var c2 = CreateInStatus(ContentStatus.Draft);
        var c3 = CreateInStatus(ContentStatus.Review);
        SetupContents(c1, c2, c3);
        _workflowEngine.Setup(x => x.TransitionAsync(
                c1.Id, ContentStatus.Approved, null, ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
        _workflowEngine.Setup(x => x.TransitionAsync(
                c2.Id, ContentStatus.Approved, null, ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed, "Not in Review"));
        _workflowEngine.Setup(x => x.TransitionAsync(
                c3.Id, ContentStatus.Approved, null, ActorType.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var result = await _service.BatchApproveAsync([c1.Id, c2.Id, c3.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
    }
}
