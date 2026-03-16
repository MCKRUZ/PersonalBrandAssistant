using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;

public class WriterAgentCapabilityTests
{
    private readonly Mock<IPromptTemplateService> _promptService;
    private readonly Mock<ISidecarClient> _sidecarClient;
    private readonly WriterAgentCapability _capability;

    public WriterAgentCapabilityTests()
    {
        _promptService = new Mock<IPromptTemplateService>();
        _sidecarClient = new Mock<ISidecarClient>();
        _capability = new WriterAgentCapability(
            new Mock<ILogger<WriterAgentCapability>>().Object);
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
    public void Type_IsWriter()
    {
        Assert.Equal(AgentCapabilityType.Writer, _capability.Type);
    }

    [Fact]
    public void DefaultModelTier_IsStandard()
    {
        Assert.Equal(ModelTier.Standard, _capability.DefaultModelTier);
    }

    [Fact]
    public async Task ExecuteAsync_LoadsSystemAndTaskTemplates()
    {
        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("system prompt");
        _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("task prompt");

        SetupSidecarResponse("# My Blog Post\n\nThis is the body content.");

        var context = CreateContext();
        await _capability.ExecuteAsync(context, CancellationToken.None);

        _promptService.Verify(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()), Times.Once);
        _promptService.Verify(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UsesTemplateFromParameters()
    {
        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("system prompt");
        _promptService.Setup(p => p.RenderAsync("writer", "article", It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("article prompt");

        SetupSidecarResponse("# Article Title\n\nArticle body.");

        var context = CreateContext(new Dictionary<string, string> { ["template"] = "article" });
        await _capability.ExecuteAsync(context, CancellationToken.None);

        _promptService.Verify(p => p.RenderAsync("writer", "article", It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsAgentOutputWithCreatesContentTrue()
    {
        SetupPrompts("writer", "blog-post");
        SetupSidecarResponse("# My Title\n\nContent body here.");

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CreatesContent);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesTitleFromResponse()
    {
        SetupPrompts("writer", "blog-post");
        SetupSidecarResponse("# The Great Title\n\nSome content body.");

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("The Great Title", result.Value!.Title);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailureOnEmptyResponse()
    {
        SetupPrompts("writer", "blog-post");
        SetupSidecarResponse("");

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_InjectsBrandProfileIntoVariables()
    {
        Dictionary<string, object>? capturedVars = null;
        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
            .Callback<string, string, Dictionary<string, object>>((_, _, v) => capturedVars = v)
            .ReturnsAsync("system prompt");
        _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("task prompt");

        SetupSidecarResponse("# Title\n\nBody");

        var context = CreateContext();
        await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(capturedVars);
        Assert.True(capturedVars.ContainsKey("brand"));
        Assert.IsType<BrandProfilePromptModel>(capturedVars["brand"]);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailureOnSidecarException()
    {
        SetupPrompts("writer", "blog-post");
        _sidecarClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Sidecar connection lost"));

        var context = CreateContext();
        var result = await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("Sidecar connection lost", result.Errors[0]);
    }

    [Fact]
    public async Task ExecuteAsync_NamespacesParametersUnderTaskKey()
    {
        Dictionary<string, object>? capturedVars = null;
        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
            .Callback<string, string, Dictionary<string, object>>((_, _, v) => capturedVars = v)
            .ReturnsAsync("system prompt");
        _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("task prompt");

        SetupSidecarResponse("# Title\n\nBody");

        var context = CreateContext(new Dictionary<string, string> { ["topic"] = "AI" });
        await _capability.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(capturedVars);
        Assert.True(capturedVars.ContainsKey("task"));
        var taskParams = Assert.IsType<Dictionary<string, string>>(capturedVars["task"]);
        Assert.Equal("AI", taskParams["topic"]);
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
        if (!string.IsNullOrEmpty(text))
            yield return new ChatEvent("assistant", text, null, null);
        yield return new TaskCompleteEvent("mock-session", 100, 50);
        await Task.CompletedTask;
    }
}
