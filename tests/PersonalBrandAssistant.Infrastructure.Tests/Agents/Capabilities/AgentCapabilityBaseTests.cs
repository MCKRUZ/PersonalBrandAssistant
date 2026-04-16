using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Application.Common.Models.Skills;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;

public class AgentCapabilityBaseTests
{
    private readonly Mock<ISkillRegistry> _skillRegistry = new();
    private readonly Mock<ISidecarClient> _sidecarClient = new();
    private readonly Mock<IPromptTemplateService> _promptService = new();
    private readonly ILogger _logger = NullLogger.Instance;

    private AgentContext CreateContext(Dictionary<string, string>? parameters = null) =>
        new()
        {
            ExecutionId = Guid.NewGuid(),
            BrandProfile = TestBrandProfile.Create(),
            PromptService = _promptService.Object,
            SidecarClient = _sidecarClient.Object,
            Parameters = parameters ?? new Dictionary<string, string>(),
        };

    private TestCapability CreateCapability() =>
        new(_skillRegistry.Object, _logger);

    private void SetupSkillRegistry(string skillId, string? modelId = null, string level2Body = "Skill body {{ brand_voice_block }}")
    {
        var definition = new SkillDefinition
        {
            Id = skillId, Name = skillId, Description = "test",
            Category = "test", SkillType = "test", ModelId = modelId,
            Tags = Array.Empty<string>(), AllowedTools = Array.Empty<string>(),
            SchemaVersion = 1,
        };
        _skillRegistry.Setup(r => r.GetSkillById(skillId)).Returns(definition);
        _skillRegistry.Setup(r => r.LoadLevel2(skillId)).Returns(level2Body);
    }

    private void SetupRenderRaw(string returns = "rendered system prompt") =>
        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(returns);

    private void SetupTaskPrompt(string returns = "task prompt") =>
        _promptService.Setup(p => p.RenderAsync("writer", It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(returns);

    private void SetupSidecarResponse(string text = "Generated content")
    {
        _sidecarClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateSidecarEvents(text));
    }

    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEvents(
        string text, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(text))
            yield return new ChatEvent("assistant", text, null, null);
        yield return new TaskCompleteEvent("mock-session", 100, 50);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAsync_SkillFoundAndLevel2Loaded_UsesSkillBodyAsSystemPrompt()
    {
        const string level2Body = "## Skill body {{ brand_voice_block }}";
        SetupSkillRegistry("writer", level2Body: level2Body);
        SetupRenderRaw();
        SetupTaskPrompt();
        SetupSidecarResponse();

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        _skillRegistry.Verify(r => r.GetSkillById("writer"), Times.Once);
        _skillRegistry.Verify(r => r.LoadLevel2("writer"), Times.Once);
        _promptService.Verify(p => p.RenderRawAsync(level2Body, It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkillNotFound_ReturnsFailureResult()
    {
        _skillRegistry.Setup(r => r.GetSkillById("writer")).Returns((SkillDefinition?)null);

        var result = await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("writer", result.Errors[0]);
    }

    [Fact]
    public async Task ExecuteAsync_SkillWithModelId_PassesModelIdToSidecar()
    {
        SetupSkillRegistry("writer", modelId: "claude-opus-4-6");
        SetupRenderRaw();
        SetupTaskPrompt();

        string? capturedModelId = "not-set";
        _sidecarClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, string?, string?, CancellationToken>(
                (_, _, _, modelId, _) => capturedModelId = modelId)
            .Returns(CreateSidecarEvents("Output"));

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal("claude-opus-4-6", capturedModelId);
    }

    [Fact]
    public async Task ExecuteAsync_SkillWithNullModelId_PassesNullModelIdToSidecar()
    {
        SetupSkillRegistry("writer", modelId: null);
        SetupRenderRaw();
        SetupTaskPrompt();

        string? capturedModelId = "not-set";
        _sidecarClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, string?, string?, CancellationToken>(
                (_, _, _, modelId, _) => capturedModelId = modelId)
            .Returns(CreateSidecarEvents("Output"));

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Null(capturedModelId);
    }

    [Fact]
    public async Task ExecuteAsync_SkillBodyContainsBrandVoiceBlock_IsRendered()
    {
        const string level2Body = "You are a writer. {{ brand_voice_block }} Write great content.";
        SetupSkillRegistry("writer", level2Body: level2Body);
        SetupRenderRaw("rendered with brand voice");
        SetupTaskPrompt();
        SetupSidecarResponse();

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        _promptService.Verify(p => p.RenderRawAsync(level2Body, It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RenderedSystemPrompt_ContainsBrandVoiceContent()
    {
        SetupSkillRegistry("writer");
        SetupRenderRaw("Actual brand voice content body");
        SetupTaskPrompt();

        string? capturedSystemPrompt = null;
        _sidecarClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, string?, string?, CancellationToken>(
                (_, systemPrompt, _, _, _) => capturedSystemPrompt = systemPrompt)
            .Returns(CreateSidecarEvents("Output"));

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal("Actual brand voice content body", capturedSystemPrompt);
    }

    [Fact]
    public async Task ExecuteAsync_TaskPromptRendering_StillUsesLiquidTemplates()
    {
        SetupSkillRegistry("writer");
        SetupRenderRaw();
        SetupSidecarResponse();

        string? capturedTemplate = null;
        _promptService.Setup(p => p.RenderAsync("writer", It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Callback<string, string, Dictionary<string, object>>((_, template, _) => capturedTemplate = template)
            .ReturnsAsync("task prompt");

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        _promptService.Verify(p => p.RenderAsync("writer", It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
        Assert.Equal("blog-post", capturedTemplate);
    }

    [Theory]
    [InlineData("writer")]
    [InlineData("social")]
    [InlineData("repurpose")]
    [InlineData("engagement")]
    [InlineData("analytics")]
    public async Task ExecuteAsync_AllFiveCapabilities_ReturnSuccessWithMockedSidecar(string skillId)
    {
        var registry = new Mock<ISkillRegistry>();
        var definition = new SkillDefinition
        {
            Id = skillId, Name = skillId, Description = "test",
            Category = "test", SkillType = "test",
            Tags = Array.Empty<string>(), AllowedTools = Array.Empty<string>(),
            SchemaVersion = 1,
        };
        registry.Setup(r => r.GetSkillById(skillId)).Returns(definition);
        registry.Setup(r => r.LoadLevel2(skillId)).Returns("Skill body {{ brand_voice_block }}");

        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("rendered system");
        _promptService.Setup(p => p.RenderAsync(skillId, It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("task prompt");
        SetupSidecarResponse("Generated content from sidecar");

        IAgentCapability capability = skillId switch
        {
            "writer" => new WriterAgentCapability(registry.Object, new Mock<ILogger<WriterAgentCapability>>().Object),
            "social" => new SocialAgentCapability(registry.Object, new Mock<ILogger<SocialAgentCapability>>().Object),
            "repurpose" => new RepurposeAgentCapability(registry.Object, new Mock<ILogger<RepurposeAgentCapability>>().Object),
            "engagement" => new EngagementAgentCapability(registry.Object, new Mock<ILogger<EngagementAgentCapability>>().Object),
            "analytics" => new AnalyticsAgentCapability(registry.Object, new Mock<ILogger<AnalyticsAgentCapability>>().Object),
            _ => throw new ArgumentOutOfRangeException(nameof(skillId))
        };

        var result = await capability.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Minimal concrete subclass for testing AgentCapabilityBase directly.</summary>
    private sealed class TestCapability : AgentCapabilityBase
    {
        public TestCapability(ISkillRegistry registry, ILogger logger) : base(registry, logger) { }
        public override AgentCapabilityType Type => AgentCapabilityType.Writer;
        public override ModelTier DefaultModelTier => ModelTier.Standard;
        protected override string AgentName => "writer";
        protected override string SkillName => "writer";
        protected override string DefaultTemplate => "blog-post";
        protected override bool CreatesContent => true;
    }
}
