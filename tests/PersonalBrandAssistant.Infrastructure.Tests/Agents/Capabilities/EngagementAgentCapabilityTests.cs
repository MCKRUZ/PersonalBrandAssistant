using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;

public class EngagementAgentCapabilityTests
{
    private readonly Mock<IPromptTemplateService> _promptService;
    private readonly Mock<ISidecarClient> _sidecarClient;
    private readonly EngagementAgentCapability _capability;

    public EngagementAgentCapabilityTests()
    {
        _promptService = new Mock<IPromptTemplateService>();
        _sidecarClient = new Mock<ISidecarClient>();
        _capability = new EngagementAgentCapability(
            new Mock<ILogger<EngagementAgentCapability>>().Object);
    }

    private AgentContext CreateContext(Dictionary<string, string>? parameters = null) =>
        new()
        {
            ExecutionId = Guid.NewGuid(),
            BrandProfile = TestBrandProfile.Create(),
            PromptService = _promptService.Object,
            SidecarClient = _sidecarClient.Object,
            Parameters = parameters ?? new Dictionary<string, string>(),
        };

    [Fact]
    public void Type_IsEngagement()
    {
        Assert.Equal(AgentCapabilityType.Engagement, _capability.Type);
    }

    [Fact]
    public void DefaultModelTier_IsFast()
    {
        Assert.Equal(ModelTier.Fast, _capability.DefaultModelTier);
    }

    [Fact]
    public async Task ExecuteAsync_SetsCreatesContentFalse()
    {
        SetupPrompts("engagement", "response-suggestion");
        SetupSidecarResponse("Here are some response suggestions...");

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CreatesContent);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuggestionsAsOutput()
    {
        SetupPrompts("engagement", "response-suggestion");
        SetupSidecarResponse("Suggestion 1: Reply with gratitude\nSuggestion 2: Ask a follow-up");

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("Suggestion", result.Value!.GeneratedText);
    }

    private void SetupPrompts(string agent, string template)
    {
        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("system prompt");
        _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("task prompt");
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

    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEvents(
        string text, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ChatEvent("assistant", text, null, null);
        yield return new TaskCompleteEvent("mock-session", 100, 50);
        await Task.CompletedTask;
    }
}
