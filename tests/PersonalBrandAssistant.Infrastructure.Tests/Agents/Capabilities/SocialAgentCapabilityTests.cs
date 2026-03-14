using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;

public class SocialAgentCapabilityTests
{
    private readonly Mock<IPromptTemplateService> _promptService;
    private readonly Mock<IChatClient> _chatClient;
    private readonly SocialAgentCapability _capability;

    public SocialAgentCapabilityTests()
    {
        _promptService = new Mock<IPromptTemplateService>();
        _chatClient = new Mock<IChatClient>();
        _capability = new SocialAgentCapability(
            new Mock<ILogger<SocialAgentCapability>>().Object);
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
    public void Type_IsSocial()
    {
        Assert.Equal(AgentCapabilityType.Social, _capability.Type);
    }

    [Fact]
    public void DefaultModelTier_IsFast()
    {
        Assert.Equal(ModelTier.Fast, _capability.DefaultModelTier);
    }

    [Fact]
    public async Task ExecuteAsync_LoadsSocialTemplates()
    {
        SetupPrompts("social", "post");
        SetupChatResponse("{\"text\": \"Check out this post!\", \"hashtags\": [\"#tech\"]}");

        var context = CreateContext();
        await _capability.ExecuteAsync(context, CancellationToken.None);

        _promptService.Verify(p => p.RenderAsync("social", "system", It.IsAny<Dictionary<string, object>>()), Times.Once);
        _promptService.Verify(p => p.RenderAsync("social", "post", It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SetsCreatesContentTrue()
    {
        SetupPrompts("social", "post");
        SetupChatResponse("{\"text\": \"Social post content\", \"hashtags\": [\"#brand\"]}");

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CreatesContent);
    }

    [Fact]
    public async Task ExecuteAsync_UsesThreadTemplateFromParameters()
    {
        SetupPrompts("social", "thread");
        SetupChatResponse("{\"text\": \"Thread content\", \"hashtags\": []}");

        var context = CreateContext(new Dictionary<string, string> { ["template"] = "thread" });
        await _capability.ExecuteAsync(context, CancellationToken.None);

        _promptService.Verify(p => p.RenderAsync("social", "thread", It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsGeneratedText()
    {
        SetupPrompts("social", "post");
        SetupChatResponse("{\"text\": \"Amazing content here!\", \"hashtags\": [\"#ai\", \"#tech\"]}");

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("Amazing content here!", result.Value!.GeneratedText);
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
