using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

public class ContentPipelineTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<ISidecarClient> _sidecarClient = new();
    private readonly Mock<IBrandVoiceService> _brandVoiceService = new();
    private readonly Mock<IWorkflowEngine> _workflowEngine = new();
    private readonly Mock<IPipelineEventBroadcaster> _broadcaster = new();
    private readonly Mock<ILogger<ContentPipeline>> _logger = new();

    private ContentPipeline CreatePipeline() =>
        new(_dbContext.Object, _sidecarClient.Object, _brandVoiceService.Object,
            _workflowEngine.Object, _broadcaster.Object, _logger.Object);

    private void SetupContentsDbSet(List<Content> contents)
    {
        var mockDbSet = contents.AsQueryable().BuildMockDbSet();
        mockDbSet.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns<object[], CancellationToken>((keys, _) =>
                new ValueTask<Content?>(contents.FirstOrDefault(c => c.Id == (Guid)keys[0])));
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private void SetupBrandProfiles()
    {
        var profile = new BrandProfile
        {
            Name = "Test Brand",
            PersonaDescription = "Test persona",
            StyleGuidelines = "Be concise",
            IsActive = true,
        };
        profile.ToneDescriptors = ["professional"];
        profile.Topics = ["tech"];
        profile.ExampleContent = [];
        profile.VocabularyPreferences = new VocabularyConfig
        {
            PreferredTerms = ["AI"],
            AvoidTerms = ["synergy"],
        };

        var dbSet = new List<BrandProfile> { profile }.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.BrandProfiles).Returns(dbSet.Object);
    }

    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEvents(
        string text, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ChatEvent("assistant", text, null, null);
        yield return new TaskCompleteEvent("session-1", 100, 50);
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEventsWithFile(
        string text, string filePath, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ChatEvent("assistant", text, null, null);
        yield return new FileChangeEvent(filePath, "created");
        yield return new TaskCompleteEvent("session-1", 200, 100);
        await Task.CompletedTask;
    }

    private void SetupSidecarResponse(string text)
    {
        _sidecarClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateSidecarEvents(text));
    }

    // --- CreateFromTopicAsync ---

    [Fact]
    public async Task CreateFromTopicAsync_ValidRequest_CreatesContentInDraftStatus()
    {
        Content? captured = null;
        var mockDbSet = new List<Content>().AsQueryable().BuildMockDbSet();
        mockDbSet.Setup(d => d.Add(It.IsAny<Content>()))
            .Callback<Content>(c => captured = c);
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var pipeline = CreatePipeline();
        var request = new ContentCreationRequest(
            ContentType.BlogPost, "AI in branding", null, null, null, null);

        var result = await pipeline.CreateFromTopicAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(ContentStatus.Draft, captured!.Status);
        Assert.Contains("AI in branding", captured.Metadata.AiGenerationContext!);
    }

    [Fact]
    public async Task CreateFromTopicAsync_EmptyTopic_ReturnsValidationFailure()
    {
        SetupContentsDbSet([]);
        var pipeline = CreatePipeline();
        var request = new ContentCreationRequest(
            ContentType.BlogPost, "", null, null, null, null);

        var result = await pipeline.CreateFromTopicAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task CreateFromTopicAsync_WithParentContentId_SetsParentOnContent()
    {
        Content? captured = null;
        var mockDbSet = new List<Content>().AsQueryable().BuildMockDbSet();
        mockDbSet.Setup(d => d.Add(It.IsAny<Content>()))
            .Callback<Content>(c => captured = c);
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var parentId = Guid.NewGuid();
        var pipeline = CreatePipeline();
        var request = new ContentCreationRequest(
            ContentType.SocialPost, "Test topic", null, null, parentId, null);

        await pipeline.CreateFromTopicAsync(request, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(parentId, captured!.ParentContentId);
    }

    [Fact]
    public async Task CreateFromTopicAsync_WithTargetPlatforms_SetsTargetPlatformsOnContent()
    {
        Content? captured = null;
        var mockDbSet = new List<Content>().AsQueryable().BuildMockDbSet();
        mockDbSet.Setup(d => d.Add(It.IsAny<Content>()))
            .Callback<Content>(c => captured = c);
        _dbContext.Setup(x => x.Contents).Returns(mockDbSet.Object);
        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var pipeline = CreatePipeline();
        var request = new ContentCreationRequest(
            ContentType.SocialPost, "Test topic", null,
            [PlatformType.TwitterX, PlatformType.LinkedIn], null, null);

        await pipeline.CreateFromTopicAsync(request, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal([PlatformType.TwitterX, PlatformType.LinkedIn], captured!.TargetPlatforms);
    }

    // --- GenerateOutlineAsync ---

    [Fact]
    public async Task GenerateOutlineAsync_ValidContentId_SendsOutlineTaskToSidecar()
    {
        var content = Content.Create(ContentType.BlogPost, string.Empty);
        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(new { topic = "AI trends" });
        SetupContentsDbSet([content]);
        SetupSidecarResponse("## Outline\n1. Intro\n2. Body\n3. Conclusion");

        var pipeline = CreatePipeline();
        await pipeline.GenerateOutlineAsync(content.Id, CancellationToken.None);

        _sidecarClient.Verify(c => c.SendTaskAsync(
            It.Is<string>(s => s.Contains("AI trends") && s.Contains("outline", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateOutlineAsync_ValidContentId_StoresOutlineInMetadata()
    {
        var content = Content.Create(ContentType.BlogPost, string.Empty);
        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(new { topic = "AI trends" });
        SetupContentsDbSet([content]);
        SetupSidecarResponse("## Outline\n1. Intro\n2. Body");

        var pipeline = CreatePipeline();
        await pipeline.GenerateOutlineAsync(content.Id, CancellationToken.None);

        var ctx = JsonSerializer.Deserialize<JsonElement>(content.Metadata.AiGenerationContext!);
        Assert.True(ctx.TryGetProperty("outline", out var outline));
        Assert.Contains("Outline", outline.GetString());
    }

    [Fact]
    public async Task GenerateOutlineAsync_ContentNotFound_ReturnsNotFound()
    {
        SetupContentsDbSet([]);
        var pipeline = CreatePipeline();

        var result = await pipeline.GenerateOutlineAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task GenerateOutlineAsync_ReturnsOutlineText()
    {
        var content = Content.Create(ContentType.BlogPost, string.Empty);
        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(new { topic = "AI" });
        SetupContentsDbSet([content]);
        SetupSidecarResponse("1. Intro\n2. Body\n3. Outro");

        var pipeline = CreatePipeline();
        var result = await pipeline.GenerateOutlineAsync(content.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("1. Intro\n2. Body\n3. Outro", result.Value);
    }

    // --- GenerateDraftAsync ---

    [Fact]
    public async Task GenerateDraftAsync_SocialPost_UpdatesContentBody()
    {
        var content = Content.Create(ContentType.SocialPost, string.Empty);
        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(
            new { topic = "AI tips", outline = "1. Tip one 2. Tip two" });
        SetupContentsDbSet([content]);
        SetupBrandProfiles();
        SetupSidecarResponse("Here are 5 AI tips for your brand...");

        var pipeline = CreatePipeline();
        await pipeline.GenerateDraftAsync(content.Id, CancellationToken.None);

        Assert.Equal("Here are 5 AI tips for your brand...", content.Body);
    }

    [Fact]
    public async Task GenerateDraftAsync_BlogPost_CapturesFilePath()
    {
        var content = Content.Create(ContentType.BlogPost, string.Empty);
        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(
            new { topic = "AI blog", outline = "1. Intro" });
        SetupContentsDbSet([content]);
        SetupBrandProfiles();
        _sidecarClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateSidecarEventsWithFile("<html>Blog content</html>", "/blog/ai-post.html"));

        var pipeline = CreatePipeline();
        await pipeline.GenerateDraftAsync(content.Id, CancellationToken.None);

        Assert.Equal("/blog/ai-post.html", content.Metadata.PlatformSpecificData["filePath"]);
    }

    [Fact]
    public async Task GenerateDraftAsync_ContentNotFound_ReturnsNotFound()
    {
        SetupContentsDbSet([]);
        var pipeline = CreatePipeline();

        var result = await pipeline.GenerateDraftAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // --- ValidateVoiceAsync ---

    [Fact]
    public async Task ValidateVoiceAsync_DelegatesToBrandVoiceService()
    {
        var content = Content.Create(ContentType.BlogPost, "Some content");
        SetupContentsDbSet([content]);
        var score = new BrandVoiceScore(85, 90, 80, 85, [], []);
        _brandVoiceService.Setup(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BrandVoiceScore>.Success(score));

        var pipeline = CreatePipeline();
        await pipeline.ValidateVoiceAsync(content.Id, CancellationToken.None);

        _brandVoiceService.Verify(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateVoiceAsync_ReturnsBrandVoiceScore()
    {
        var content = Content.Create(ContentType.BlogPost, "Some content");
        SetupContentsDbSet([content]);
        var score = new BrandVoiceScore(85, 90, 80, 85, [], []);
        _brandVoiceService.Setup(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BrandVoiceScore>.Success(score));

        var pipeline = CreatePipeline();
        var result = await pipeline.ValidateVoiceAsync(content.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(85, result.Value!.OverallScore);
    }

    [Fact]
    public async Task ValidateVoiceAsync_ContentNotFound_ReturnsNotFound()
    {
        SetupContentsDbSet([]);
        var pipeline = CreatePipeline();

        var result = await pipeline.ValidateVoiceAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // --- SubmitForReviewAsync ---

    [Fact]
    public async Task SubmitForReviewAsync_TransitionsContentToReview()
    {
        var content = Content.Create(ContentType.BlogPost, "Content body");
        SetupContentsDbSet([content]);
        _workflowEngine.Setup(w => w.TransitionAsync(
                content.Id, ContentStatus.Review, It.IsAny<string?>(),
                It.IsAny<ActorType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
        _workflowEngine.Setup(w => w.ShouldAutoApproveAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var pipeline = CreatePipeline();
        var result = await pipeline.SubmitForReviewAsync(content.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _workflowEngine.Verify(w => w.TransitionAsync(
            content.Id, ContentStatus.Review, It.IsAny<string?>(),
            It.IsAny<ActorType>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitForReviewAsync_AutonomousLevel_AutoApproves()
    {
        var content = Content.Create(ContentType.BlogPost, "Content body");
        SetupContentsDbSet([content]);
        _workflowEngine.Setup(w => w.TransitionAsync(
                content.Id, It.IsAny<ContentStatus>(), It.IsAny<string?>(),
                It.IsAny<ActorType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
        _workflowEngine.Setup(w => w.ShouldAutoApproveAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var pipeline = CreatePipeline();
        await pipeline.SubmitForReviewAsync(content.Id, CancellationToken.None);

        _workflowEngine.Verify(w => w.TransitionAsync(
            content.Id, ContentStatus.Approved, It.IsAny<string?>(),
            It.IsAny<ActorType>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitForReviewAsync_ContentNotFound_ReturnsNotFound()
    {
        SetupContentsDbSet([]);
        var pipeline = CreatePipeline();

        var result = await pipeline.SubmitForReviewAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }
}
