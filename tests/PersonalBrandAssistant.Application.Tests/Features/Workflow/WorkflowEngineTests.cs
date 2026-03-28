using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.Events;
using PersonalBrandAssistant.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Tests.Features.Workflow;

public class WorkflowEngineTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProvider = new();
    private readonly Mock<ILogger<WorkflowEngine>> _logger = new();
    private readonly WorkflowEngine _engine;

    public WorkflowEngineTests()
    {
        _dateTimeProvider.Setup(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);
        _engine = new WorkflowEngine(_dbContext.Object, _dateTimeProvider.Object, _logger.Object);
    }

    private void SetupContents(params ContentEntity[] contents)
    {
        var mockDbSet = contents.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
    }

    private void SetupTransitionLogs()
    {
        var mockDbSet = new List<WorkflowTransitionLog>().AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.WorkflowTransitionLogs).Returns(mockDbSet.Object);
    }

    private static ContentEntity CreateContentInState(
        ContentStatus target,
        AutonomyLevel autonomyLevel = AutonomyLevel.Manual,
        Guid? parentContentId = null)
    {
        var content = ContentEntity.Create(ContentType.BlogPost, "Test body",
            capturedAutonomyLevel: autonomyLevel);
        if (parentContentId.HasValue)
            content.ParentContentId = parentContentId.Value;

        TransitionToState(content, target);
        content.ClearDomainEvents();
        return content;
    }

    private static void TransitionToState(ContentEntity content, ContentStatus target)
    {
        if (content.Status == target) return;

        var path = target switch
        {
            ContentStatus.Draft => Array.Empty<ContentStatus>(),
            ContentStatus.Review => [ContentStatus.Review],
            ContentStatus.Approved => [ContentStatus.Review, ContentStatus.Approved],
            ContentStatus.Scheduled => [ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled],
            ContentStatus.Publishing => [ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing],
            ContentStatus.Published => [ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Published],
            ContentStatus.Failed => [ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Failed],
            ContentStatus.Archived => [ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Published, ContentStatus.Archived],
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

        foreach (var step in path)
            content.TransitionTo(step);
    }

    // -- Valid transition tests --

    [Fact]
    public async Task TransitionAsync_DraftToReview_Succeeds()
    {
        var content = CreateContentInState(ContentStatus.Draft);
        SetupContents(content);
        SetupTransitionLogs();

        var result = await _engine.TransitionAsync(content.Id, ContentStatus.Review);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContentStatus.Review, content.Status);
    }

    [Fact]
    public async Task TransitionAsync_ReviewToApproved_SucceedsForManual()
    {
        var content = CreateContentInState(ContentStatus.Review);
        SetupContents(content);
        SetupTransitionLogs();

        var result = await _engine.TransitionAsync(content.Id, ContentStatus.Approved);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContentStatus.Approved, content.Status);
    }

    [Fact]
    public async Task TransitionAsync_InvalidTransition_ReturnsFailure()
    {
        var content = CreateContentInState(ContentStatus.Draft);
        SetupContents(content);
        SetupTransitionLogs();

        var result = await _engine.TransitionAsync(content.Id, ContentStatus.Published);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task TransitionAsync_NonexistentContent_ReturnsNotFound()
    {
        SetupContents();
        SetupTransitionLogs();

        var result = await _engine.TransitionAsync(Guid.NewGuid(), ContentStatus.Review);

        Assert.False(result.IsSuccess);
        Assert.Equal(Application.Common.Errors.ErrorCode.NotFound, result.ErrorCode);
    }

    // -- Audit log tests --

    [Fact]
    public async Task TransitionAsync_CreatesWorkflowTransitionLog()
    {
        var content = CreateContentInState(ContentStatus.Draft);
        SetupContents(content);
        var logs = new List<WorkflowTransitionLog>();
        var mockLogDbSet = logs.AsQueryable().BuildMockDbSet();
        mockLogDbSet.Setup(x => x.Add(It.IsAny<WorkflowTransitionLog>()))
            .Callback<WorkflowTransitionLog>(logs.Add);
        _dbContext.Setup(x => x.WorkflowTransitionLogs).Returns(mockLogDbSet.Object);

        await _engine.TransitionAsync(content.Id, ContentStatus.Review);

        Assert.Single(logs);
        var log = logs[0];
        Assert.Equal(content.Id, log.ContentId);
        Assert.Equal(ContentStatus.Draft, log.FromStatus);
        Assert.Equal(ContentStatus.Review, log.ToStatus);
    }

    [Fact]
    public async Task TransitionAsync_WithReason_RecordsReasonInLog()
    {
        var content = CreateContentInState(ContentStatus.Review);
        SetupContents(content);
        var logs = new List<WorkflowTransitionLog>();
        var mockLogDbSet = logs.AsQueryable().BuildMockDbSet();
        mockLogDbSet.Setup(x => x.Add(It.IsAny<WorkflowTransitionLog>()))
            .Callback<WorkflowTransitionLog>(logs.Add);
        _dbContext.Setup(x => x.WorkflowTransitionLogs).Returns(mockLogDbSet.Object);

        await _engine.TransitionAsync(content.Id, ContentStatus.Draft, reason: "Needs revision");

        Assert.Single(logs);
        Assert.Equal("Needs revision", logs[0].Reason);
    }

    [Fact]
    public async Task TransitionAsync_RecordsActorType()
    {
        var content = CreateContentInState(ContentStatus.Draft);
        SetupContents(content);
        var logs = new List<WorkflowTransitionLog>();
        var mockLogDbSet = logs.AsQueryable().BuildMockDbSet();
        mockLogDbSet.Setup(x => x.Add(It.IsAny<WorkflowTransitionLog>()))
            .Callback<WorkflowTransitionLog>(logs.Add);
        _dbContext.Setup(x => x.WorkflowTransitionLogs).Returns(mockLogDbSet.Object);

        await _engine.TransitionAsync(content.Id, ContentStatus.Review, actor: ActorType.Agent);

        Assert.Single(logs);
        Assert.Equal(ActorType.Agent, logs[0].ActorType);
    }

    // -- Autonomy guard tests --

    [Fact]
    public async Task TransitionAsync_Autonomous_AutoApprovesFromDraftToReview()
    {
        var content = CreateContentInState(ContentStatus.Draft, AutonomyLevel.Autonomous);
        SetupContents(content);
        var logs = new List<WorkflowTransitionLog>();
        var mockLogDbSet = logs.AsQueryable().BuildMockDbSet();
        mockLogDbSet.Setup(x => x.Add(It.IsAny<WorkflowTransitionLog>()))
            .Callback<WorkflowTransitionLog>(logs.Add);
        _dbContext.Setup(x => x.WorkflowTransitionLogs).Returns(mockLogDbSet.Object);

        var result = await _engine.TransitionAsync(content.Id, ContentStatus.Review);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContentStatus.Approved, content.Status);
        Assert.Equal(2, logs.Count); // Draft->Review, Review->Approved
    }

    [Fact]
    public async Task TransitionAsync_SemiAuto_WithPublishedParent_AutoApproves()
    {
        var parent = CreateContentInState(ContentStatus.Published);
        var content = CreateContentInState(ContentStatus.Draft, AutonomyLevel.SemiAuto, parent.Id);
        SetupContents(content, parent);
        var logs = new List<WorkflowTransitionLog>();
        var mockLogDbSet = logs.AsQueryable().BuildMockDbSet();
        mockLogDbSet.Setup(x => x.Add(It.IsAny<WorkflowTransitionLog>()))
            .Callback<WorkflowTransitionLog>(logs.Add);
        _dbContext.Setup(x => x.WorkflowTransitionLogs).Returns(mockLogDbSet.Object);

        var result = await _engine.TransitionAsync(content.Id, ContentStatus.Review);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContentStatus.Approved, content.Status);
    }

    [Fact]
    public async Task TransitionAsync_SemiAuto_WithoutParent_DoesNotAutoApprove()
    {
        var content = CreateContentInState(ContentStatus.Draft, AutonomyLevel.SemiAuto);
        SetupContents(content);
        SetupTransitionLogs();

        var result = await _engine.TransitionAsync(content.Id, ContentStatus.Review);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContentStatus.Review, content.Status);
    }

    [Fact]
    public async Task TransitionAsync_Manual_StaysInReview()
    {
        var content = CreateContentInState(ContentStatus.Draft, AutonomyLevel.Manual);
        SetupContents(content);
        SetupTransitionLogs();

        var result = await _engine.TransitionAsync(content.Id, ContentStatus.Review);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContentStatus.Review, content.Status);
    }

    [Fact]
    public async Task TransitionAsync_Assisted_RequiresExplicitApproval()
    {
        var content = CreateContentInState(ContentStatus.Draft, AutonomyLevel.Assisted);
        SetupContents(content);
        SetupTransitionLogs();

        var result = await _engine.TransitionAsync(content.Id, ContentStatus.Review);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContentStatus.Review, content.Status);
    }

    // -- Domain event tests --

    [Fact]
    public async Task TransitionAsync_ReviewToApproved_RaisesContentApprovedEvent()
    {
        var content = CreateContentInState(ContentStatus.Review);
        SetupContents(content);
        SetupTransitionLogs();

        await _engine.TransitionAsync(content.Id, ContentStatus.Approved);

        var domainEvent = Assert.Single(content.DomainEvents);
        Assert.IsType<ContentApprovedEvent>(domainEvent);
    }

    // -- Concurrency tests --

    [Fact]
    public async Task TransitionAsync_ConcurrencyConflict_ReturnsConflict()
    {
        var content = CreateContentInState(ContentStatus.Draft);
        SetupContents(content);
        SetupTransitionLogs();
        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Conflict"));

        var result = await _engine.TransitionAsync(content.Id, ContentStatus.Review);

        Assert.False(result.IsSuccess);
        Assert.Equal(Application.Common.Errors.ErrorCode.Conflict, result.ErrorCode);
    }

    // -- GetAllowedTransitions tests --

    [Fact]
    public async Task GetAllowedTransitionsAsync_Draft_ReturnsReviewAndArchived()
    {
        var content = CreateContentInState(ContentStatus.Draft);
        SetupContents(content);

        var result = await _engine.GetAllowedTransitionsAsync(content.Id);

        Assert.True(result.IsSuccess);
        Assert.Contains(ContentStatus.Review, result.Value!);
        Assert.Contains(ContentStatus.Archived, result.Value!);
        Assert.Equal(2, result.Value!.Length);
    }

    [Fact]
    public async Task GetAllowedTransitionsAsync_Review_ReturnsDraftApprovedArchived()
    {
        var content = CreateContentInState(ContentStatus.Review);
        SetupContents(content);

        var result = await _engine.GetAllowedTransitionsAsync(content.Id);

        Assert.True(result.IsSuccess);
        Assert.Contains(ContentStatus.Draft, result.Value!);
        Assert.Contains(ContentStatus.Approved, result.Value!);
        Assert.Contains(ContentStatus.Archived, result.Value!);
        Assert.Equal(3, result.Value!.Length);
    }

    // -- ShouldAutoApprove tests --

    [Fact]
    public async Task ShouldAutoApproveAsync_Autonomous_ReturnsTrue()
    {
        var content = CreateContentInState(ContentStatus.Review, AutonomyLevel.Autonomous);
        SetupContents(content);

        var result = await _engine.ShouldAutoApproveAsync(content.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldAutoApproveAsync_Manual_ReturnsFalse()
    {
        var content = CreateContentInState(ContentStatus.Review, AutonomyLevel.Manual);
        SetupContents(content);

        var result = await _engine.ShouldAutoApproveAsync(content.Id);

        Assert.False(result);
    }
}
