using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;

public class RepurposeAgentCapabilityTests
{
    private readonly Mock<IPromptTemplateService> _promptService;
    private readonly Mock<IChatClient> _chatClient;
    private readonly RepurposeAgentCapability _capability;

    public RepurposeAgentCapabilityTests()
    {
        _promptService = new Mock<IPromptTemplateService>();
        _chatClient = new Mock<IChatClient>();
        _capability = new RepurposeAgentCapability(
            new Mock<ILogger<RepurposeAgentCapability>>().Object);
    }

    private AgentContext CreateContext(Dictionary<string, string>? parameters = null, ContentPromptModel? content = null) =>
        new()
        {
            ExecutionId = Guid.NewGuid(),
            BrandProfile = TestBrandProfile.Create(),
            Content = content ?? new ContentPromptModel
            {
                Title = "Original Blog Post",
                Body = "This is the original content to repurpose.",
                ContentType = ContentType.BlogPost,
                Status = ContentStatus.Published,
                TargetPlatforms = [PlatformType.TwitterX]
            },
            PromptService = _promptService.Object,
            ChatClient = _chatClient.Object,
            Parameters = parameters ?? new Dictionary<string, string> { ["template"] = "blog-to-thread" },
            ModelTier = ModelTier.Standard
        };

    [Fact]
    public void Type_IsRepurpose()
    {
        Assert.Equal(AgentCapabilityType.Repurpose, _capability.Type);
    }

    [Fact]
    public void DefaultModelTier_IsStandard()
    {
        Assert.Equal(ModelTier.Standard, _capability.DefaultModelTier);
    }

    [Fact]
    public async Task ExecuteAsync_LoadsRepurposeTemplates()
    {
        SetupPrompts("repurpose", "blog-to-thread");
        SetupChatResponse("Repurposed thread content");

        var context = CreateContext();
        await _capability.ExecuteAsync(context, CancellationToken.None);

        _promptService.Verify(p => p.RenderAsync("repurpose", "system", It.IsAny<Dictionary<string, object>>()), Times.Once);
        _promptService.Verify(p => p.RenderAsync("repurpose", "blog-to-thread", It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SetsCreatesContentTrue()
    {
        SetupPrompts("repurpose", "blog-to-thread");
        SetupChatResponse("Thread content from blog.");

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CreatesContent);
    }

    [Fact]
    public async Task ExecuteAsync_PassesSourceContentInVariables()
    {
        Dictionary<string, object>? capturedVars = null;
        _promptService.Setup(p => p.RenderAsync("repurpose", "blog-to-thread", It.IsAny<Dictionary<string, object>>()))
            .Callback<string, string, Dictionary<string, object>>((_, _, v) => capturedVars = v)
            .ReturnsAsync("task prompt");
        _promptService.Setup(p => p.RenderAsync("repurpose", "system", It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("system prompt");

        SetupChatResponse("Repurposed content.");

        var context = CreateContext();
        await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(capturedVars);
        Assert.True(capturedVars.ContainsKey("content"));
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
