using System.Runtime.CompilerServices;
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
    private readonly Mock<ISidecarClient> _sidecarClient;
    private readonly RepurposeAgentCapability _capability;

    public RepurposeAgentCapabilityTests()
    {
        _promptService = new Mock<IPromptTemplateService>();
        _sidecarClient = new Mock<ISidecarClient>();
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
            SidecarClient = _sidecarClient.Object,
            Parameters = parameters ?? new Dictionary<string, string> { ["template"] = "blog-to-thread" },
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
        SetupSidecarResponse("Repurposed thread content");

        var context = CreateContext();
        await _capability.ExecuteAsync(context, CancellationToken.None);

        _promptService.Verify(p => p.RenderAsync("repurpose", "system", It.IsAny<Dictionary<string, object>>()), Times.Once);
        _promptService.Verify(p => p.RenderAsync("repurpose", "blog-to-thread", It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SetsCreatesContentTrue()
    {
        SetupPrompts("repurpose", "blog-to-thread");
        SetupSidecarResponse("Thread content from blog.");

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

        SetupSidecarResponse("Repurposed content.");

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
