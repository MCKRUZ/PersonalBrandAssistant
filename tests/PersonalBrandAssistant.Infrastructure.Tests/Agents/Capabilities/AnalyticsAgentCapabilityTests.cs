using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;

public class AnalyticsAgentCapabilityTests
{
    private readonly Mock<IPromptTemplateService> _promptService;
    private readonly Mock<IChatClient> _chatClient;
    private readonly AnalyticsAgentCapability _capability;

    public AnalyticsAgentCapabilityTests()
    {
        _promptService = new Mock<IPromptTemplateService>();
        _chatClient = new Mock<IChatClient>();
        _capability = new AnalyticsAgentCapability(
            new Mock<ILogger<AnalyticsAgentCapability>>().Object);
    }

    private AgentContext CreateContext(Dictionary<string, string>? parameters = null) =>
        new()
        {
            ExecutionId = Guid.NewGuid(),
            BrandProfile = TestBrandProfile.Create(),
            PromptService = _promptService.Object,
            ChatClient = _chatClient.Object,
            Parameters = parameters ?? new Dictionary<string, string>(),
            ModelTier = ModelTier.Fast
        };

    [Fact]
    public void Type_IsAnalytics()
    {
        Assert.Equal(AgentCapabilityType.Analytics, _capability.Type);
    }

    [Fact]
    public void DefaultModelTier_IsFast()
    {
        Assert.Equal(ModelTier.Fast, _capability.DefaultModelTier);
    }

    [Fact]
    public async Task ExecuteAsync_SetsCreatesContentFalse()
    {
        SetupPrompts("analytics", "performance-insights");
        SetupChatResponse("Your top performing content was...");

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CreatesContent);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsRecommendations()
    {
        SetupPrompts("analytics", "performance-insights");
        SetupChatResponse("Recommendation: Post more on Tuesdays for better engagement.");

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("Recommendation", result.Value!.GeneratedText);
    }

    private void SetupPrompts(string agent, string template)
    {
        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("system prompt");
        _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("task prompt");
    }

    private void SetupChatResponse(string text)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        _chatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }
}
