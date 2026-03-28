using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;
using PersonalBrandAssistant.Infrastructure.Agents;
using PersonalBrandAssistant.Infrastructure.Tests.Helpers;

namespace PersonalBrandAssistant.Infrastructure.Tests.Agents;

public class AgentOrchestratorTests
{
    private readonly Mock<ITokenTracker> _tokenTracker = new();
    private readonly Mock<ISidecarClient> _sidecarClient = new();
    private readonly Mock<IPromptTemplateService> _promptTemplateService = new();
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<IWorkflowEngine> _workflowEngine = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<ILogger<AgentOrchestrator>> _logger = new();

    private readonly Mock<DbSet<AgentExecution>> _executionDbSet = new();
    private readonly Mock<DbSet<Content>> _contentDbSet = new();

    private readonly AgentOrchestrationOptions _options = new()
    {
        ExecutionTimeoutSeconds = 30,
    };

    private AgentOrchestrator CreateOrchestrator(
        IEnumerable<IAgentCapability>? capabilities = null)
    {
        var brandProfile = new BrandProfile
        {
            Name = "Test Brand",
            PersonaDescription = "Test persona",
            StyleGuidelines = "Be concise",
            IsActive = true,
        };
        brandProfile.ToneDescriptors = ["professional"];
        brandProfile.Topics = ["tech"];
        brandProfile.ExampleContent = ["Sample post"];
        brandProfile.VocabularyPreferences = new VocabularyConfig
        {
            PreferredTerms = ["AI"],
            AvoidTerms = ["synergy"],
        };

        var brandProfileDbSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(
            new List<BrandProfile> { brandProfile });

        _dbContext.Setup(x => x.AgentExecutions).Returns(_executionDbSet.Object);
        _dbContext.Setup(x => x.Contents).Returns(_contentDbSet.Object);
        _dbContext.Setup(x => x.BrandProfiles).Returns(brandProfileDbSet.Object);
        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _workflowEngine
            .Setup(x => x.TransitionAsync(
                It.IsAny<Guid>(), It.IsAny<ContentStatus>(), It.IsAny<string?>(),
                It.IsAny<ActorType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        return new AgentOrchestrator(
            capabilities ?? [],
            _tokenTracker.Object,
            _sidecarClient.Object,
            _promptTemplateService.Object,
            _dbContext.Object,
            _workflowEngine.Object,
            _notificationService.Object,
            Options.Create(_options),
            _logger.Object);
    }

    private static Mock<IAgentCapability> CreateCapabilityMock(
        AgentCapabilityType type,
        ModelTier defaultTier = ModelTier.Standard,
        AgentOutput? output = null)
    {
        var mock = new Mock<IAgentCapability>();
        mock.Setup(x => x.Type).Returns(type);
        mock.Setup(x => x.DefaultModelTier).Returns(defaultTier);
        mock.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentOutput>.Success(output ?? new AgentOutput
            {
                GeneratedText = "Test output",
                CreatesContent = false,
            }));
        return mock;
    }

    // --- Task Routing Tests ---

    [Fact]
    public async Task ExecuteAsync_RoutesWriterTask_ToWriterCapability()
    {
        var writerCapability = CreateCapabilityMock(AgentCapabilityType.Writer);
        var orchestrator = CreateOrchestrator([writerCapability.Object]);
        var task = new AgentTask(AgentCapabilityType.Writer, null, new());

        await orchestrator.ExecuteAsync(task, CancellationToken.None);

        writerCapability.Verify(
            x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RoutesSocialTask_ToSocialCapability()
    {
        var socialCapability = CreateCapabilityMock(AgentCapabilityType.Social, ModelTier.Fast);
        var orchestrator = CreateOrchestrator([socialCapability.Object]);
        var task = new AgentTask(AgentCapabilityType.Social, null, new());

        await orchestrator.ExecuteAsync(task, CancellationToken.None);

        socialCapability.Verify(
            x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(AgentCapabilityType.Writer)]
    [InlineData(AgentCapabilityType.Social)]
    [InlineData(AgentCapabilityType.Repurpose)]
    [InlineData(AgentCapabilityType.Engagement)]
    [InlineData(AgentCapabilityType.Analytics)]
    public async Task ExecuteAsync_RoutesEachType_ToCorrectCapability(AgentCapabilityType type)
    {
        var capability = CreateCapabilityMock(type);
        var orchestrator = CreateOrchestrator([capability.Object]);
        var task = new AgentTask(type, null, new());

        await orchestrator.ExecuteAsync(task, CancellationToken.None);

        capability.Verify(
            x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsValidationFailed_WhenOverBudget()
    {
        _tokenTracker.Setup(x => x.IsOverBudgetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var orchestrator = CreateOrchestrator();
        var task = new AgentTask(AgentCapabilityType.Writer, null, new());

        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesAgentExecution_BeforeCallingCapability()
    {
        var callOrder = new List<string>();

        _executionDbSet.Setup(x => x.Add(It.IsAny<AgentExecution>()))
            .Callback<AgentExecution>(e =>
            {
                callOrder.Add("add_execution");
                Assert.Equal(AgentExecutionStatus.Pending, e.Status);
            });

        var capability = CreateCapabilityMock(AgentCapabilityType.Writer);
        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .Callback<AgentContext, CancellationToken>((_, _) => callOrder.Add("execute"))
            .ReturnsAsync(Result<AgentOutput>.Success(new AgentOutput
            {
                GeneratedText = "output",
                CreatesContent = false,
            }));

        var orchestrator = CreateOrchestrator([capability.Object]);
        var task = new AgentTask(AgentCapabilityType.Writer, null, new());

        await orchestrator.ExecuteAsync(task, CancellationToken.None);

        Assert.Equal(["add_execution", "execute"], callOrder);
    }

    [Fact]
    public async Task ExecuteAsync_SetsExecutionToCompleted_OnSuccess()
    {
        AgentExecution? capturedExecution = null;
        _executionDbSet.Setup(x => x.Add(It.IsAny<AgentExecution>()))
            .Callback<AgentExecution>(e => capturedExecution = e);

        var capability = CreateCapabilityMock(AgentCapabilityType.Writer);
        var orchestrator = CreateOrchestrator([capability.Object]);
        var task = new AgentTask(AgentCapabilityType.Writer, null, new());

        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedExecution);
        Assert.Equal(AgentExecutionStatus.Completed, capturedExecution!.Status);
    }

    [Fact]
    public async Task ExecuteAsync_SetsExecutionToFailed_OnCapabilityFailure()
    {
        AgentExecution? capturedExecution = null;
        _executionDbSet.Setup(x => x.Add(It.IsAny<AgentExecution>()))
            .Callback<AgentExecution>(e => capturedExecution = e);

        var capability = new Mock<IAgentCapability>();
        capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
        capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Standard);
        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentOutput>.Failure(ErrorCode.InternalError, "Prompt error"));

        var orchestrator = CreateOrchestrator([capability.Object]);
        var task = new AgentTask(AgentCapabilityType.Writer, null, new());

        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(capturedExecution);
        Assert.Equal(AgentExecutionStatus.Failed, capturedExecution!.Status);
    }

    // --- Content Creation Tests ---

    [Fact]
    public async Task ExecuteAsync_CreatesContent_WhenOutputCreatesContentIsTrue()
    {
        var output = new AgentOutput
        {
            GeneratedText = "Blog post body",
            Title = "My Post",
            CreatesContent = true,
        };
        var capability = CreateCapabilityMock(AgentCapabilityType.Writer, output: output);
        var orchestrator = CreateOrchestrator([capability.Object]);
        var task = new AgentTask(AgentCapabilityType.Writer, null, new());

        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _contentDbSet.Verify(x => x.Add(It.IsAny<Content>()), Times.Once);
        Assert.NotNull(result.Value!.CreatedContentId);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCreateContent_WhenOutputCreatesContentIsFalse()
    {
        var output = new AgentOutput
        {
            GeneratedText = "Analysis report",
            CreatesContent = false,
        };
        var capability = CreateCapabilityMock(AgentCapabilityType.Analytics, output: output);
        var orchestrator = CreateOrchestrator([capability.Object]);
        var task = new AgentTask(AgentCapabilityType.Analytics, null, new());

        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _contentDbSet.Verify(x => x.Add(It.IsAny<Content>()), Times.Never);
        Assert.Null(result.Value!.CreatedContentId);
    }

    [Fact]
    public async Task ExecuteAsync_SubmitsToWorkflow_WhenContentIsCreated()
    {
        var output = new AgentOutput
        {
            GeneratedText = "Post content",
            CreatesContent = true,
        };
        _workflowEngine
            .Setup(x => x.TransitionAsync(
                It.IsAny<Guid>(), ContentStatus.Review, It.IsAny<string?>(),
                ActorType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var capability = CreateCapabilityMock(AgentCapabilityType.Social, ModelTier.Fast, output);
        var orchestrator = CreateOrchestrator([capability.Object]);
        var task = new AgentTask(AgentCapabilityType.Social, null, new());

        await orchestrator.ExecuteAsync(task, CancellationToken.None);

        _workflowEngine.Verify(
            x => x.TransitionAsync(
                It.IsAny<Guid>(), ContentStatus.Review,
                It.Is<string?>(s => s != null && s.Contains("Agent")),
                ActorType.Agent,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- Token Tracking Tests ---

    [Fact]
    public async Task ExecuteAsync_RecordsTokenUsage_FromSidecarEvents()
    {
        var output = new AgentOutput
        {
            GeneratedText = "output",
            CreatesContent = false,
            InputTokens = 200,
            OutputTokens = 75,
        };
        var capability = CreateCapabilityMock(AgentCapabilityType.Writer, output: output);
        var orchestrator = CreateOrchestrator([capability.Object]);
        var task = new AgentTask(AgentCapabilityType.Writer, null, new());

        await orchestrator.ExecuteAsync(task, CancellationToken.None);

        _tokenTracker.Verify(
            x => x.RecordUsageAsync(
                It.IsAny<Guid>(),
                "sidecar",
                200,
                75,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- Failure Tests ---

    [Fact]
    public async Task ExecuteAsync_FailsAndNotifies_OnCapabilityException()
    {
        var capability = new Mock<IAgentCapability>();
        capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
        capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Standard);
        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Sidecar connection lost"));

        var orchestrator = CreateOrchestrator([capability.Object]);
        var task = new AgentTask(AgentCapabilityType.Writer, null, new());

        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);

        Assert.False(result.IsSuccess);
        _notificationService.Verify(
            x => x.SendAsync(
                NotificationType.ContentFailed, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PassesSidecarClient_InAgentContext()
    {
        AgentContext? capturedContext = null;
        var capability = new Mock<IAgentCapability>();
        capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
        capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Standard);
        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .Callback<AgentContext, CancellationToken>((ctx, _) => capturedContext = ctx)
            .ReturnsAsync(Result<AgentOutput>.Success(new AgentOutput
            {
                GeneratedText = "output",
                CreatesContent = false,
            }));

        var orchestrator = CreateOrchestrator([capability.Object]);
        var task = new AgentTask(AgentCapabilityType.Writer, null, new());

        await orchestrator.ExecuteAsync(task, CancellationToken.None);

        Assert.NotNull(capturedContext);
        Assert.Same(_sidecarClient.Object, capturedContext!.SidecarClient);
        Assert.Null(capturedContext.SessionId);
    }

    // --- Status Query Tests ---

    [Fact]
    public async Task GetExecutionStatusAsync_ReturnsExecution_ById()
    {
        var execution = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard);
        var executionId = execution.Id;

        _executionDbSet.Setup(x => x.FindAsync(new object[] { executionId }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.GetExecutionStatusAsync(executionId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(executionId, result.Value!.Id);
    }

    [Fact]
    public async Task GetExecutionStatusAsync_ReturnsNotFound_ForUnknownId()
    {
        _executionDbSet.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentExecution?)null);

        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.GetExecutionStatusAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }
}
