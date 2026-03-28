diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
new file mode 100644
index 0000000..57150df
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
@@ -0,0 +1,85 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+public abstract class AgentCapabilityBase : IAgentCapability
+{
+    private readonly ILogger _logger;
+
+    protected AgentCapabilityBase(ILogger logger)
+    {
+        _logger = logger;
+    }
+
+    public abstract AgentCapabilityType Type { get; }
+    public abstract ModelTier DefaultModelTier { get; }
+    protected abstract string AgentName { get; }
+    protected abstract string DefaultTemplate { get; }
+    protected abstract bool CreatesContent { get; }
+
+    public async Task<Result<AgentOutput>> ExecuteAsync(AgentContext context, CancellationToken ct)
+    {
+        try
+        {
+            var templateName = context.Parameters.GetValueOrDefault("template", DefaultTemplate);
+            var variables = BuildVariables(context);
+
+            var systemPrompt = await context.PromptService.RenderAsync(AgentName, "system", variables);
+            var taskPrompt = await context.PromptService.RenderAsync(AgentName, templateName, variables);
+
+            var messages = new List<ChatMessage>
+            {
+                new(ChatRole.System, systemPrompt),
+                new(ChatRole.User, taskPrompt)
+            };
+
+            var response = await context.ChatClient.GetResponseAsync(messages, cancellationToken: ct);
+            var responseText = response.Text ?? string.Empty;
+
+            if (string.IsNullOrWhiteSpace(responseText))
+            {
+                _logger.LogWarning("{Capability} received empty response from chat client", Type);
+                return Result<AgentOutput>.Failure(ErrorCode.InternalError,
+                    $"{Type} capability received empty response from model");
+            }
+
+            return BuildOutput(responseText, context);
+        }
+        catch (Exception ex)
+        {
+            _logger.LogError(ex, "{Capability} failed during execution", Type);
+            return Result<AgentOutput>.Failure(ErrorCode.InternalError,
+                $"{Type} capability failed: {ex.Message}");
+        }
+    }
+
+    protected virtual Dictionary<string, object> BuildVariables(AgentContext context)
+    {
+        var variables = new Dictionary<string, object>
+        {
+            ["brand"] = context.BrandProfile
+        };
+
+        if (context.Content is not null)
+            variables["content"] = context.Content;
+
+        foreach (var param in context.Parameters)
+            variables[param.Key] = param.Value;
+
+        return variables;
+    }
+
+    protected virtual Result<AgentOutput> BuildOutput(string responseText, AgentContext context)
+    {
+        return Result<AgentOutput>.Success(new AgentOutput
+        {
+            GeneratedText = responseText,
+            CreatesContent = CreatesContent
+        });
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AnalyticsAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AnalyticsAgentCapability.cs
new file mode 100644
index 0000000..731c8e6
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AnalyticsAgentCapability.cs
@@ -0,0 +1,16 @@
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+public sealed class AnalyticsAgentCapability : AgentCapabilityBase
+{
+    public AnalyticsAgentCapability(ILogger<AnalyticsAgentCapability> logger) : base(logger) { }
+
+    public override AgentCapabilityType Type => AgentCapabilityType.Analytics;
+    public override ModelTier DefaultModelTier => ModelTier.Fast;
+    protected override string AgentName => "analytics";
+    protected override string DefaultTemplate => "performance-insights";
+    protected override bool CreatesContent => false;
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/EngagementAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/EngagementAgentCapability.cs
new file mode 100644
index 0000000..c8cce98
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/EngagementAgentCapability.cs
@@ -0,0 +1,16 @@
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+public sealed class EngagementAgentCapability : AgentCapabilityBase
+{
+    public EngagementAgentCapability(ILogger<EngagementAgentCapability> logger) : base(logger) { }
+
+    public override AgentCapabilityType Type => AgentCapabilityType.Engagement;
+    public override ModelTier DefaultModelTier => ModelTier.Fast;
+    protected override string AgentName => "engagement";
+    protected override string DefaultTemplate => "response-suggestion";
+    protected override bool CreatesContent => false;
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/RepurposeAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/RepurposeAgentCapability.cs
new file mode 100644
index 0000000..5aa6855
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/RepurposeAgentCapability.cs
@@ -0,0 +1,16 @@
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+public sealed class RepurposeAgentCapability : AgentCapabilityBase
+{
+    public RepurposeAgentCapability(ILogger<RepurposeAgentCapability> logger) : base(logger) { }
+
+    public override AgentCapabilityType Type => AgentCapabilityType.Repurpose;
+    public override ModelTier DefaultModelTier => ModelTier.Standard;
+    protected override string AgentName => "repurpose";
+    protected override string DefaultTemplate => "blog-to-thread";
+    protected override bool CreatesContent => true;
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/SocialAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/SocialAgentCapability.cs
new file mode 100644
index 0000000..6dba2c0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/SocialAgentCapability.cs
@@ -0,0 +1,16 @@
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+public sealed class SocialAgentCapability : AgentCapabilityBase
+{
+    public SocialAgentCapability(ILogger<SocialAgentCapability> logger) : base(logger) { }
+
+    public override AgentCapabilityType Type => AgentCapabilityType.Social;
+    public override ModelTier DefaultModelTier => ModelTier.Fast;
+    protected override string AgentName => "social";
+    protected override string DefaultTemplate => "post";
+    protected override bool CreatesContent => true;
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs
new file mode 100644
index 0000000..b730260
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs
@@ -0,0 +1,39 @@
+using System.Text.RegularExpressions;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+public sealed partial class WriterAgentCapability : AgentCapabilityBase
+{
+    public WriterAgentCapability(ILogger<WriterAgentCapability> logger) : base(logger) { }
+
+    public override AgentCapabilityType Type => AgentCapabilityType.Writer;
+    public override ModelTier DefaultModelTier => ModelTier.Standard;
+    protected override string AgentName => "writer";
+    protected override string DefaultTemplate => "blog-post";
+    protected override bool CreatesContent => true;
+
+    protected override Result<AgentOutput> BuildOutput(string responseText, AgentContext context)
+    {
+        var title = ExtractTitle(responseText);
+
+        return Result<AgentOutput>.Success(new AgentOutput
+        {
+            GeneratedText = responseText,
+            Title = title,
+            CreatesContent = true
+        });
+    }
+
+    private static string? ExtractTitle(string text)
+    {
+        var match = TitlePattern().Match(text);
+        return match.Success ? match.Groups[1].Value.Trim() : null;
+    }
+
+    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
+    private static partial Regex TitlePattern();
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs
new file mode 100644
index 0000000..4144ab8
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs
@@ -0,0 +1,91 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
+
+public class AnalyticsAgentCapabilityTests
+{
+    private readonly Mock<IPromptTemplateService> _promptService;
+    private readonly Mock<IChatClient> _chatClient;
+    private readonly AnalyticsAgentCapability _capability;
+
+    public AnalyticsAgentCapabilityTests()
+    {
+        _promptService = new Mock<IPromptTemplateService>();
+        _chatClient = new Mock<IChatClient>();
+        _capability = new AnalyticsAgentCapability(
+            new Mock<ILogger<AnalyticsAgentCapability>>().Object);
+    }
+
+    private AgentContext CreateContext(Dictionary<string, string>? parameters = null) =>
+        new()
+        {
+            ExecutionId = Guid.NewGuid(),
+            BrandProfile = TestBrandProfile.Create(),
+            PromptService = _promptService.Object,
+            ChatClient = _chatClient.Object,
+            Parameters = parameters ?? new Dictionary<string, string>(),
+            ModelTier = ModelTier.Fast
+        };
+
+    [Fact]
+    public void Type_IsAnalytics()
+    {
+        Assert.Equal(AgentCapabilityType.Analytics, _capability.Type);
+    }
+
+    [Fact]
+    public void DefaultModelTier_IsFast()
+    {
+        Assert.Equal(ModelTier.Fast, _capability.DefaultModelTier);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SetsCreatesContentFalse()
+    {
+        SetupPrompts("analytics", "performance-insights");
+        SetupChatResponse("Your top performing content was...");
+
+        var context = CreateContext();
+        var result = await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.False(result.Value!.CreatesContent);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_ReturnsRecommendations()
+    {
+        SetupPrompts("analytics", "performance-insights");
+        SetupChatResponse("Recommendation: Post more on Tuesdays for better engagement.");
+
+        var context = CreateContext();
+        var result = await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Contains("Recommendation", result.Value!.GeneratedText);
+    }
+
+    private void SetupPrompts(string agent, string template)
+    {
+        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("system prompt");
+        _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("task prompt");
+    }
+
+    private void SetupChatResponse(string text)
+    {
+        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
+        _chatClient.Setup(c => c.GetResponseAsync(
+                It.IsAny<IList<ChatMessage>>(),
+                It.IsAny<ChatOptions>(),
+                It.IsAny<CancellationToken>()))
+            .ReturnsAsync(response);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs
new file mode 100644
index 0000000..5626378
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs
@@ -0,0 +1,91 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
+
+public class EngagementAgentCapabilityTests
+{
+    private readonly Mock<IPromptTemplateService> _promptService;
+    private readonly Mock<IChatClient> _chatClient;
+    private readonly EngagementAgentCapability _capability;
+
+    public EngagementAgentCapabilityTests()
+    {
+        _promptService = new Mock<IPromptTemplateService>();
+        _chatClient = new Mock<IChatClient>();
+        _capability = new EngagementAgentCapability(
+            new Mock<ILogger<EngagementAgentCapability>>().Object);
+    }
+
+    private AgentContext CreateContext(Dictionary<string, string>? parameters = null) =>
+        new()
+        {
+            ExecutionId = Guid.NewGuid(),
+            BrandProfile = TestBrandProfile.Create(),
+            PromptService = _promptService.Object,
+            ChatClient = _chatClient.Object,
+            Parameters = parameters ?? new Dictionary<string, string>(),
+            ModelTier = ModelTier.Fast
+        };
+
+    [Fact]
+    public void Type_IsEngagement()
+    {
+        Assert.Equal(AgentCapabilityType.Engagement, _capability.Type);
+    }
+
+    [Fact]
+    public void DefaultModelTier_IsFast()
+    {
+        Assert.Equal(ModelTier.Fast, _capability.DefaultModelTier);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SetsCreatesContentFalse()
+    {
+        SetupPrompts("engagement", "response-suggestion");
+        SetupChatResponse("Here are some response suggestions...");
+
+        var context = CreateContext();
+        var result = await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.False(result.Value!.CreatesContent);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_ReturnsSuggestionsAsOutput()
+    {
+        SetupPrompts("engagement", "response-suggestion");
+        SetupChatResponse("Suggestion 1: Reply with gratitude\nSuggestion 2: Ask a follow-up");
+
+        var context = CreateContext();
+        var result = await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Contains("Suggestion", result.Value!.GeneratedText);
+    }
+
+    private void SetupPrompts(string agent, string template)
+    {
+        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("system prompt");
+        _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("task prompt");
+    }
+
+    private void SetupChatResponse(string text)
+    {
+        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
+        _chatClient.Setup(c => c.GetResponseAsync(
+                It.IsAny<IList<ChatMessage>>(),
+                It.IsAny<ChatOptions>(),
+                It.IsAny<CancellationToken>()))
+            .ReturnsAsync(response);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs
new file mode 100644
index 0000000..a10d606
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs
@@ -0,0 +1,118 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
+
+public class RepurposeAgentCapabilityTests
+{
+    private readonly Mock<IPromptTemplateService> _promptService;
+    private readonly Mock<IChatClient> _chatClient;
+    private readonly RepurposeAgentCapability _capability;
+
+    public RepurposeAgentCapabilityTests()
+    {
+        _promptService = new Mock<IPromptTemplateService>();
+        _chatClient = new Mock<IChatClient>();
+        _capability = new RepurposeAgentCapability(
+            new Mock<ILogger<RepurposeAgentCapability>>().Object);
+    }
+
+    private AgentContext CreateContext(Dictionary<string, string>? parameters = null, ContentPromptModel? content = null) =>
+        new()
+        {
+            ExecutionId = Guid.NewGuid(),
+            BrandProfile = TestBrandProfile.Create(),
+            Content = content ?? new ContentPromptModel
+            {
+                Title = "Original Blog Post",
+                Body = "This is the original content to repurpose.",
+                ContentType = ContentType.BlogPost,
+                Status = ContentStatus.Published,
+                TargetPlatforms = [PlatformType.TwitterX]
+            },
+            PromptService = _promptService.Object,
+            ChatClient = _chatClient.Object,
+            Parameters = parameters ?? new Dictionary<string, string> { ["template"] = "blog-to-thread" },
+            ModelTier = ModelTier.Standard
+        };
+
+    [Fact]
+    public void Type_IsRepurpose()
+    {
+        Assert.Equal(AgentCapabilityType.Repurpose, _capability.Type);
+    }
+
+    [Fact]
+    public void DefaultModelTier_IsStandard()
+    {
+        Assert.Equal(ModelTier.Standard, _capability.DefaultModelTier);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_LoadsRepurposeTemplates()
+    {
+        SetupPrompts("repurpose", "blog-to-thread");
+        SetupChatResponse("Repurposed thread content");
+
+        var context = CreateContext();
+        await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        _promptService.Verify(p => p.RenderAsync("repurpose", "system", It.IsAny<Dictionary<string, object>>()), Times.Once);
+        _promptService.Verify(p => p.RenderAsync("repurpose", "blog-to-thread", It.IsAny<Dictionary<string, object>>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SetsCreatesContentTrue()
+    {
+        SetupPrompts("repurpose", "blog-to-thread");
+        SetupChatResponse("Thread content from blog.");
+
+        var context = CreateContext();
+        var result = await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.True(result.Value!.CreatesContent);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_PassesSourceContentInVariables()
+    {
+        Dictionary<string, object>? capturedVars = null;
+        _promptService.Setup(p => p.RenderAsync("repurpose", "blog-to-thread", It.IsAny<Dictionary<string, object>>()))
+            .Callback<string, string, Dictionary<string, object>>((_, _, v) => capturedVars = v)
+            .ReturnsAsync("task prompt");
+        _promptService.Setup(p => p.RenderAsync("repurpose", "system", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("system prompt");
+
+        SetupChatResponse("Repurposed content.");
+
+        var context = CreateContext();
+        await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.NotNull(capturedVars);
+        Assert.True(capturedVars.ContainsKey("content"));
+    }
+
+    private void SetupPrompts(string agent, string template)
+    {
+        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("system prompt");
+        _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("task prompt");
+    }
+
+    private void SetupChatResponse(string text)
+    {
+        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
+        _chatClient.Setup(c => c.GetResponseAsync(
+                It.IsAny<IList<ChatMessage>>(),
+                It.IsAny<ChatOptions>(),
+                It.IsAny<CancellationToken>()))
+            .ReturnsAsync(response);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs
new file mode 100644
index 0000000..97db332
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs
@@ -0,0 +1,116 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
+
+public class SocialAgentCapabilityTests
+{
+    private readonly Mock<IPromptTemplateService> _promptService;
+    private readonly Mock<IChatClient> _chatClient;
+    private readonly SocialAgentCapability _capability;
+
+    public SocialAgentCapabilityTests()
+    {
+        _promptService = new Mock<IPromptTemplateService>();
+        _chatClient = new Mock<IChatClient>();
+        _capability = new SocialAgentCapability(
+            new Mock<ILogger<SocialAgentCapability>>().Object);
+    }
+
+    private AgentContext CreateContext(Dictionary<string, string>? parameters = null) =>
+        new()
+        {
+            ExecutionId = Guid.NewGuid(),
+            BrandProfile = TestBrandProfile.Create(),
+            PromptService = _promptService.Object,
+            ChatClient = _chatClient.Object,
+            Parameters = parameters ?? new Dictionary<string, string>(),
+            ModelTier = ModelTier.Fast
+        };
+
+    [Fact]
+    public void Type_IsSocial()
+    {
+        Assert.Equal(AgentCapabilityType.Social, _capability.Type);
+    }
+
+    [Fact]
+    public void DefaultModelTier_IsFast()
+    {
+        Assert.Equal(ModelTier.Fast, _capability.DefaultModelTier);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_LoadsSocialTemplates()
+    {
+        SetupPrompts("social", "post");
+        SetupChatResponse("{\"text\": \"Check out this post!\", \"hashtags\": [\"#tech\"]}");
+
+        var context = CreateContext();
+        await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        _promptService.Verify(p => p.RenderAsync("social", "system", It.IsAny<Dictionary<string, object>>()), Times.Once);
+        _promptService.Verify(p => p.RenderAsync("social", "post", It.IsAny<Dictionary<string, object>>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SetsCreatesContentTrue()
+    {
+        SetupPrompts("social", "post");
+        SetupChatResponse("{\"text\": \"Social post content\", \"hashtags\": [\"#brand\"]}");
+
+        var context = CreateContext();
+        var result = await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.True(result.Value!.CreatesContent);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_UsesThreadTemplateFromParameters()
+    {
+        SetupPrompts("social", "thread");
+        SetupChatResponse("{\"text\": \"Thread content\", \"hashtags\": []}");
+
+        var context = CreateContext(new Dictionary<string, string> { ["template"] = "thread" });
+        await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        _promptService.Verify(p => p.RenderAsync("social", "thread", It.IsAny<Dictionary<string, object>>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_ReturnsGeneratedText()
+    {
+        SetupPrompts("social", "post");
+        SetupChatResponse("{\"text\": \"Amazing content here!\", \"hashtags\": [\"#ai\", \"#tech\"]}");
+
+        var context = CreateContext();
+        var result = await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Contains("Amazing content here!", result.Value!.GeneratedText);
+    }
+
+    private void SetupPrompts(string agent, string template)
+    {
+        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("system prompt");
+        _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("task prompt");
+    }
+
+    private void SetupChatResponse(string text)
+    {
+        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
+        _chatClient.Setup(c => c.GetResponseAsync(
+                It.IsAny<IList<ChatMessage>>(),
+                It.IsAny<ChatOptions>(),
+                It.IsAny<CancellationToken>()))
+            .ReturnsAsync(response);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/TestBrandProfile.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/TestBrandProfile.cs
new file mode 100644
index 0000000..b55f62c
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/TestBrandProfile.cs
@@ -0,0 +1,19 @@
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
+
+internal static class TestBrandProfile
+{
+    public static BrandProfilePromptModel Create() =>
+        new()
+        {
+            Name = "Test Brand",
+            PersonaDescription = "Test persona",
+            ToneDescriptors = ["professional"],
+            StyleGuidelines = "Be concise",
+            PreferredTerms = ["innovation"],
+            AvoidedTerms = ["synergy"],
+            Topics = ["tech"],
+            ExampleContent = ["Example post"]
+        };
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs
new file mode 100644
index 0000000..fdfbb27
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs
@@ -0,0 +1,169 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
+
+public class WriterAgentCapabilityTests
+{
+    private readonly Mock<IPromptTemplateService> _promptService;
+    private readonly Mock<IChatClient> _chatClient;
+    private readonly WriterAgentCapability _capability;
+
+    public WriterAgentCapabilityTests()
+    {
+        _promptService = new Mock<IPromptTemplateService>();
+        _chatClient = new Mock<IChatClient>();
+        _capability = new WriterAgentCapability(
+            new Mock<ILogger<WriterAgentCapability>>().Object);
+    }
+
+    private AgentContext CreateContext(Dictionary<string, string>? parameters = null) =>
+        new()
+        {
+            ExecutionId = Guid.NewGuid(),
+            BrandProfile = CreateBrandProfile(),
+            PromptService = _promptService.Object,
+            ChatClient = _chatClient.Object,
+            Parameters = parameters ?? new Dictionary<string, string>(),
+            ModelTier = ModelTier.Standard
+        };
+
+    private static BrandProfilePromptModel CreateBrandProfile() =>
+        new()
+        {
+            Name = "Test Brand",
+            PersonaDescription = "Test persona",
+            ToneDescriptors = ["professional"],
+            StyleGuidelines = "Be concise",
+            PreferredTerms = ["innovation"],
+            AvoidedTerms = ["synergy"],
+            Topics = ["tech"],
+            ExampleContent = ["Example post"]
+        };
+
+    [Fact]
+    public void Type_IsWriter()
+    {
+        Assert.Equal(AgentCapabilityType.Writer, _capability.Type);
+    }
+
+    [Fact]
+    public void DefaultModelTier_IsStandard()
+    {
+        Assert.Equal(ModelTier.Standard, _capability.DefaultModelTier);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_LoadsSystemAndTaskTemplates()
+    {
+        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("system prompt");
+        _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("task prompt");
+
+        SetupChatResponse("# My Blog Post\n\nThis is the body content.");
+
+        var context = CreateContext();
+        await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        _promptService.Verify(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()), Times.Once);
+        _promptService.Verify(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_UsesTemplateFromParameters()
+    {
+        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("system prompt");
+        _promptService.Setup(p => p.RenderAsync("writer", "article", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("article prompt");
+
+        SetupChatResponse("# Article Title\n\nArticle body.");
+
+        var context = CreateContext(new Dictionary<string, string> { ["template"] = "article" });
+        await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        _promptService.Verify(p => p.RenderAsync("writer", "article", It.IsAny<Dictionary<string, object>>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_ReturnsAgentOutputWithCreatesContentTrue()
+    {
+        SetupPrompts("writer", "blog-post");
+        SetupChatResponse("# My Title\n\nContent body here.");
+
+        var context = CreateContext();
+        var result = await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.True(result.Value!.CreatesContent);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_ParsesTitleFromResponse()
+    {
+        SetupPrompts("writer", "blog-post");
+        SetupChatResponse("# The Great Title\n\nSome content body.");
+
+        var context = CreateContext();
+        var result = await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("The Great Title", result.Value!.Title);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_ReturnsFailureOnEmptyResponse()
+    {
+        SetupPrompts("writer", "blog-post");
+        SetupChatResponse("");
+
+        var context = CreateContext();
+        var result = await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_InjectsBrandProfileIntoVariables()
+    {
+        Dictionary<string, object>? capturedVars = null;
+        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
+            .Callback<string, string, Dictionary<string, object>>((_, _, v) => capturedVars = v)
+            .ReturnsAsync("system prompt");
+        _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("task prompt");
+
+        SetupChatResponse("# Title\n\nBody");
+
+        var context = CreateContext();
+        await _capability.ExecuteAsync(context, CancellationToken.None);
+
+        Assert.NotNull(capturedVars);
+        Assert.True(capturedVars.ContainsKey("brand"));
+        Assert.IsType<BrandProfilePromptModel>(capturedVars["brand"]);
+    }
+
+    private void SetupPrompts(string agent, string template)
+    {
+        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("system prompt");
+        _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("task prompt");
+    }
+
+    private void SetupChatResponse(string text)
+    {
+        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
+        _chatClient.Setup(c => c.GetResponseAsync(
+                It.IsAny<IList<ChatMessage>>(),
+                It.IsAny<ChatOptions>(),
+                It.IsAny<CancellationToken>()))
+            .ReturnsAsync(response);
+    }
+}
