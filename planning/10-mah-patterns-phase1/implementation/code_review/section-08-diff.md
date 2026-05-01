diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
index e1c41d0..c3c16e6 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
@@ -9,16 +9,19 @@ namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
 public abstract class AgentCapabilityBase : IAgentCapability
 {
+    private readonly ISkillRegistry _skillRegistry;
     private readonly ILogger _logger;
 
-    protected AgentCapabilityBase(ILogger logger)
+    protected AgentCapabilityBase(ISkillRegistry skillRegistry, ILogger logger)
     {
+        _skillRegistry = skillRegistry;
         _logger = logger;
     }
 
     public abstract AgentCapabilityType Type { get; }
     public abstract ModelTier DefaultModelTier { get; }
     protected abstract string AgentName { get; }
+    protected abstract string SkillName { get; }
     protected abstract string DefaultTemplate { get; }
     protected abstract bool CreatesContent { get; }
 
@@ -29,7 +32,13 @@ public abstract class AgentCapabilityBase : IAgentCapability
             var templateName = context.Parameters.GetValueOrDefault("template", DefaultTemplate);
             var variables = BuildVariables(context);
 
-            var systemPrompt = await context.PromptService.RenderAsync(AgentName, "system", variables);
+            var skill = _skillRegistry.GetSkillById(SkillName);
+            if (skill is null)
+                return Result<AgentOutput>.Failure(ErrorCode.InternalError,
+                    $"Skill '{SkillName}' not found in registry. Ensure the SKILL.md file is present.");
+
+            var level2Body = _skillRegistry.LoadLevel2(SkillName);
+            var systemPrompt = await context.PromptService.RenderRawAsync(level2Body, variables);
             var taskPrompt = await context.PromptService.RenderAsync(AgentName, templateName, variables);
 
             var responseBuilder = new StringBuilder();
@@ -40,7 +49,7 @@ public abstract class AgentCapabilityBase : IAgentCapability
             var cost = 0m;
             var fileChanges = new List<string>();
 
-            await foreach (var evt in context.SidecarClient.SendTaskAsync(taskPrompt, systemPrompt, context.SessionId, null, ct))
+            await foreach (var evt in context.SidecarClient.SendTaskAsync(taskPrompt, systemPrompt, context.SessionId, skill.ModelId, ct))
             {
                 switch (evt)
                 {
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AnalyticsAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AnalyticsAgentCapability.cs
index 731c8e6..6f1a774 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AnalyticsAgentCapability.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AnalyticsAgentCapability.cs
@@ -1,4 +1,5 @@
 using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
 using PersonalBrandAssistant.Domain.Enums;
 
@@ -6,11 +7,13 @@ namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
 public sealed class AnalyticsAgentCapability : AgentCapabilityBase
 {
-    public AnalyticsAgentCapability(ILogger<AnalyticsAgentCapability> logger) : base(logger) { }
+    public AnalyticsAgentCapability(ISkillRegistry skillRegistry, ILogger<AnalyticsAgentCapability> logger)
+        : base(skillRegistry, logger) { }
 
     public override AgentCapabilityType Type => AgentCapabilityType.Analytics;
     public override ModelTier DefaultModelTier => ModelTier.Fast;
     protected override string AgentName => "analytics";
+    protected override string SkillName => "analytics";
     protected override string DefaultTemplate => "performance-insights";
     protected override bool CreatesContent => false;
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/EngagementAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/EngagementAgentCapability.cs
index c8cce98..b8e84bf 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/EngagementAgentCapability.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/EngagementAgentCapability.cs
@@ -1,4 +1,5 @@
 using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
 using PersonalBrandAssistant.Domain.Enums;
 
@@ -6,11 +7,13 @@ namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
 public sealed class EngagementAgentCapability : AgentCapabilityBase
 {
-    public EngagementAgentCapability(ILogger<EngagementAgentCapability> logger) : base(logger) { }
+    public EngagementAgentCapability(ISkillRegistry skillRegistry, ILogger<EngagementAgentCapability> logger)
+        : base(skillRegistry, logger) { }
 
     public override AgentCapabilityType Type => AgentCapabilityType.Engagement;
     public override ModelTier DefaultModelTier => ModelTier.Fast;
     protected override string AgentName => "engagement";
+    protected override string SkillName => "engagement";
     protected override string DefaultTemplate => "response-suggestion";
     protected override bool CreatesContent => false;
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/RepurposeAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/RepurposeAgentCapability.cs
index 5aa6855..182c6e0 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/RepurposeAgentCapability.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/RepurposeAgentCapability.cs
@@ -1,4 +1,5 @@
 using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
 using PersonalBrandAssistant.Domain.Enums;
 
@@ -6,11 +7,13 @@ namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
 public sealed class RepurposeAgentCapability : AgentCapabilityBase
 {
-    public RepurposeAgentCapability(ILogger<RepurposeAgentCapability> logger) : base(logger) { }
+    public RepurposeAgentCapability(ISkillRegistry skillRegistry, ILogger<RepurposeAgentCapability> logger)
+        : base(skillRegistry, logger) { }
 
     public override AgentCapabilityType Type => AgentCapabilityType.Repurpose;
     public override ModelTier DefaultModelTier => ModelTier.Standard;
     protected override string AgentName => "repurpose";
+    protected override string SkillName => "repurpose";
     protected override string DefaultTemplate => "blog-to-thread";
     protected override bool CreatesContent => true;
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/SocialAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/SocialAgentCapability.cs
index 6dba2c0..9a6f77b 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/SocialAgentCapability.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/SocialAgentCapability.cs
@@ -1,4 +1,5 @@
 using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
 using PersonalBrandAssistant.Domain.Enums;
 
@@ -6,11 +7,13 @@ namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
 public sealed class SocialAgentCapability : AgentCapabilityBase
 {
-    public SocialAgentCapability(ILogger<SocialAgentCapability> logger) : base(logger) { }
+    public SocialAgentCapability(ISkillRegistry skillRegistry, ILogger<SocialAgentCapability> logger)
+        : base(skillRegistry, logger) { }
 
     public override AgentCapabilityType Type => AgentCapabilityType.Social;
     public override ModelTier DefaultModelTier => ModelTier.Fast;
     protected override string AgentName => "social";
+    protected override string SkillName => "social";
     protected override string DefaultTemplate => "post";
     protected override bool CreatesContent => true;
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs
index ed8424e..15e9627 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs
@@ -1,5 +1,6 @@
 using System.Text.RegularExpressions;
 using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
 using PersonalBrandAssistant.Domain.Enums;
 
@@ -7,11 +8,13 @@ namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
 public sealed partial class WriterAgentCapability : AgentCapabilityBase
 {
-    public WriterAgentCapability(ILogger<WriterAgentCapability> logger) : base(logger) { }
+    public WriterAgentCapability(ISkillRegistry skillRegistry, ILogger<WriterAgentCapability> logger)
+        : base(skillRegistry, logger) { }
 
     public override AgentCapabilityType Type => AgentCapabilityType.Writer;
     public override ModelTier DefaultModelTier => ModelTier.Standard;
     protected override string AgentName => "writer";
+    protected override string SkillName => "writer";
     protected override string DefaultTemplate => "blog-post";
     protected override bool CreatesContent => true;
 
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AgentCapabilityBaseTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AgentCapabilityBaseTests.cs
new file mode 100644
index 0000000..db717b7
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AgentCapabilityBaseTests.cs
@@ -0,0 +1,245 @@
+using System.Runtime.CompilerServices;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Common.Models.Skills;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
+
+public class AgentCapabilityBaseTests
+{
+    private readonly Mock<ISkillRegistry> _skillRegistry = new();
+    private readonly Mock<ISidecarClient> _sidecarClient = new();
+    private readonly Mock<IPromptTemplateService> _promptService = new();
+    private readonly ILogger _logger = NullLogger.Instance;
+
+    private AgentContext CreateContext(Dictionary<string, string>? parameters = null) =>
+        new()
+        {
+            ExecutionId = Guid.NewGuid(),
+            BrandProfile = TestBrandProfile.Create(),
+            PromptService = _promptService.Object,
+            SidecarClient = _sidecarClient.Object,
+            Parameters = parameters ?? new Dictionary<string, string>(),
+        };
+
+    private TestCapability CreateCapability() =>
+        new(_skillRegistry.Object, _logger);
+
+    private void SetupSkillRegistry(string skillId, string? modelId = null, string level2Body = "Skill body {{ brand_voice_block }}")
+    {
+        var definition = new SkillDefinition
+        {
+            Id = skillId, Name = skillId, Description = "test",
+            Category = "test", SkillType = "test", ModelId = modelId,
+            Tags = Array.Empty<string>(), AllowedTools = Array.Empty<string>(),
+            SchemaVersion = 1,
+        };
+        _skillRegistry.Setup(r => r.GetSkillById(skillId)).Returns(definition);
+        _skillRegistry.Setup(r => r.LoadLevel2(skillId)).Returns(level2Body);
+    }
+
+    private void SetupRenderRaw(string returns = "rendered system prompt") =>
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync(returns);
+
+    private void SetupTaskPrompt(string returns = "task prompt") =>
+        _promptService.Setup(p => p.RenderAsync("writer", It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync(returns);
+
+    private void SetupSidecarResponse(string text = "Generated content")
+    {
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(), It.IsAny<string?>(),
+                It.IsAny<string?>(), It.IsAny<string?>(),
+                It.IsAny<CancellationToken>()))
+            .Returns(CreateSidecarEvents(text));
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEvents(
+        string text, [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        if (!string.IsNullOrEmpty(text))
+            yield return new ChatEvent("assistant", text, null, null);
+        yield return new TaskCompleteEvent("mock-session", 100, 50);
+        await Task.CompletedTask;
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SkillFoundAndLevel2Loaded_UsesSkillBodyAsSystemPrompt()
+    {
+        const string level2Body = "## Skill body {{ brand_voice_block }}";
+        SetupSkillRegistry("writer", level2Body: level2Body);
+        SetupRenderRaw();
+        SetupTaskPrompt();
+        SetupSidecarResponse();
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        _skillRegistry.Verify(r => r.GetSkillById("writer"), Times.Once);
+        _skillRegistry.Verify(r => r.LoadLevel2("writer"), Times.Once);
+        _promptService.Verify(p => p.RenderRawAsync(level2Body, It.IsAny<Dictionary<string, object>>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SkillNotFound_ReturnsFailureResult()
+    {
+        _skillRegistry.Setup(r => r.GetSkillById("writer")).Returns((SkillDefinition?)null);
+
+        var result = await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Contains("writer", result.Errors[0]);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SkillWithModelId_PassesModelIdToSidecar()
+    {
+        SetupSkillRegistry("writer", modelId: "claude-opus-4-6");
+        SetupRenderRaw();
+        SetupTaskPrompt();
+
+        string? capturedModelId = "not-set";
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(), It.IsAny<string?>(),
+                It.IsAny<string?>(), It.IsAny<string?>(),
+                It.IsAny<CancellationToken>()))
+            .Callback<string, string?, string?, string?, CancellationToken>(
+                (_, _, _, modelId, _) => capturedModelId = modelId)
+            .Returns(CreateSidecarEvents("Output"));
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        Assert.Equal("claude-opus-4-6", capturedModelId);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SkillWithNullModelId_PassesNullModelIdToSidecar()
+    {
+        SetupSkillRegistry("writer", modelId: null);
+        SetupRenderRaw();
+        SetupTaskPrompt();
+
+        string? capturedModelId = "not-set";
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(), It.IsAny<string?>(),
+                It.IsAny<string?>(), It.IsAny<string?>(),
+                It.IsAny<CancellationToken>()))
+            .Callback<string, string?, string?, string?, CancellationToken>(
+                (_, _, _, modelId, _) => capturedModelId = modelId)
+            .Returns(CreateSidecarEvents("Output"));
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        Assert.Null(capturedModelId);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SkillBodyContainsBrandVoiceBlock_IsRendered()
+    {
+        const string level2Body = "You are a writer. {{ brand_voice_block }} Write great content.";
+        SetupSkillRegistry("writer", level2Body: level2Body);
+        SetupRenderRaw("rendered with brand voice");
+        SetupTaskPrompt();
+        SetupSidecarResponse();
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        _promptService.Verify(p => p.RenderRawAsync(level2Body, It.IsAny<Dictionary<string, object>>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_RenderedSystemPrompt_ContainsBrandVoiceContent()
+    {
+        SetupSkillRegistry("writer");
+        SetupRenderRaw("Actual brand voice content body");
+        SetupTaskPrompt();
+
+        string? capturedSystemPrompt = null;
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(), It.IsAny<string?>(),
+                It.IsAny<string?>(), It.IsAny<string?>(),
+                It.IsAny<CancellationToken>()))
+            .Callback<string, string?, string?, string?, CancellationToken>(
+                (_, systemPrompt, _, _, _) => capturedSystemPrompt = systemPrompt)
+            .Returns(CreateSidecarEvents("Output"));
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        Assert.Equal("Actual brand voice content body", capturedSystemPrompt);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_TaskPromptRendering_StillUsesLiquidTemplates()
+    {
+        SetupSkillRegistry("writer");
+        SetupRenderRaw();
+        SetupSidecarResponse();
+
+        string? capturedTemplate = null;
+        _promptService.Setup(p => p.RenderAsync("writer", It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
+            .Callback<string, string, Dictionary<string, object>>((_, template, _) => capturedTemplate = template)
+            .ReturnsAsync("task prompt");
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        _promptService.Verify(p => p.RenderAsync("writer", It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
+        Assert.Equal("blog-post", capturedTemplate);
+    }
+
+    [Theory]
+    [InlineData("writer")]
+    [InlineData("social")]
+    [InlineData("repurpose")]
+    [InlineData("engagement")]
+    [InlineData("analytics")]
+    public async Task ExecuteAsync_AllFiveCapabilities_ReturnSuccessWithMockedSidecar(string skillId)
+    {
+        var registry = new Mock<ISkillRegistry>();
+        var definition = new SkillDefinition
+        {
+            Id = skillId, Name = skillId, Description = "test",
+            Category = "test", SkillType = "test",
+            Tags = Array.Empty<string>(), AllowedTools = Array.Empty<string>(),
+            SchemaVersion = 1,
+        };
+        registry.Setup(r => r.GetSkillById(skillId)).Returns(definition);
+        registry.Setup(r => r.LoadLevel2(skillId)).Returns("Skill body {{ brand_voice_block }}");
+
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("rendered system");
+        _promptService.Setup(p => p.RenderAsync(skillId, It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("task prompt");
+        SetupSidecarResponse("Generated content from sidecar");
+
+        IAgentCapability capability = skillId switch
+        {
+            "writer" => new WriterAgentCapability(registry.Object, new Mock<ILogger<WriterAgentCapability>>().Object),
+            "social" => new SocialAgentCapability(registry.Object, new Mock<ILogger<SocialAgentCapability>>().Object),
+            "repurpose" => new RepurposeAgentCapability(registry.Object, new Mock<ILogger<RepurposeAgentCapability>>().Object),
+            "engagement" => new EngagementAgentCapability(registry.Object, new Mock<ILogger<EngagementAgentCapability>>().Object),
+            "analytics" => new AnalyticsAgentCapability(registry.Object, new Mock<ILogger<AnalyticsAgentCapability>>().Object),
+            _ => throw new ArgumentOutOfRangeException(nameof(skillId))
+        };
+
+        var result = await capability.ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+    }
+
+    /// <summary>Minimal concrete subclass for testing AgentCapabilityBase directly.</summary>
+    private sealed class TestCapability : AgentCapabilityBase
+    {
+        public TestCapability(ISkillRegistry registry, ILogger logger) : base(registry, logger) { }
+        public override AgentCapabilityType Type => AgentCapabilityType.Writer;
+        public override ModelTier DefaultModelTier => ModelTier.Standard;
+        protected override string AgentName => "writer";
+        protected override string SkillName => "writer";
+        protected override string DefaultTemplate => "blog-post";
+        protected override bool CreatesContent => true;
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs
index 890287b..cf70b9c 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs
@@ -3,6 +3,7 @@ using Microsoft.Extensions.Logging;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Common.Models.Skills;
 using PersonalBrandAssistant.Domain.Enums;
 using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
@@ -10,15 +11,18 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
 
 public class AnalyticsAgentCapabilityTests
 {
+    private readonly Mock<ISkillRegistry> _skillRegistry;
     private readonly Mock<IPromptTemplateService> _promptService;
     private readonly Mock<ISidecarClient> _sidecarClient;
     private readonly AnalyticsAgentCapability _capability;
 
     public AnalyticsAgentCapabilityTests()
     {
+        _skillRegistry = new Mock<ISkillRegistry>();
         _promptService = new Mock<IPromptTemplateService>();
         _sidecarClient = new Mock<ISidecarClient>();
         _capability = new AnalyticsAgentCapability(
+            _skillRegistry.Object,
             new Mock<ILogger<AnalyticsAgentCapability>>().Object);
     }
 
@@ -72,7 +76,16 @@ public class AnalyticsAgentCapabilityTests
 
     private void SetupPrompts(string agent, string template)
     {
-        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
+        var definition = new SkillDefinition
+        {
+            Id = agent, Name = agent, Description = "test",
+            Category = "test", SkillType = "test",
+            Tags = Array.Empty<string>(), AllowedTools = Array.Empty<string>(),
+            SchemaVersion = 1,
+        };
+        _skillRegistry.Setup(r => r.GetSkillById(agent)).Returns(definition);
+        _skillRegistry.Setup(r => r.LoadLevel2(agent)).Returns("Skill body {{ brand_voice_block }}");
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("system prompt");
         _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("task prompt");
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs
index f5cdc57..e92ae0f 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs
@@ -3,6 +3,7 @@ using Microsoft.Extensions.Logging;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Common.Models.Skills;
 using PersonalBrandAssistant.Domain.Enums;
 using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
@@ -10,15 +11,18 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
 
 public class EngagementAgentCapabilityTests
 {
+    private readonly Mock<ISkillRegistry> _skillRegistry;
     private readonly Mock<IPromptTemplateService> _promptService;
     private readonly Mock<ISidecarClient> _sidecarClient;
     private readonly EngagementAgentCapability _capability;
 
     public EngagementAgentCapabilityTests()
     {
+        _skillRegistry = new Mock<ISkillRegistry>();
         _promptService = new Mock<IPromptTemplateService>();
         _sidecarClient = new Mock<ISidecarClient>();
         _capability = new EngagementAgentCapability(
+            _skillRegistry.Object,
             new Mock<ILogger<EngagementAgentCapability>>().Object);
     }
 
@@ -72,7 +76,16 @@ public class EngagementAgentCapabilityTests
 
     private void SetupPrompts(string agent, string template)
     {
-        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
+        var definition = new SkillDefinition
+        {
+            Id = agent, Name = agent, Description = "test",
+            Category = "test", SkillType = "test",
+            Tags = Array.Empty<string>(), AllowedTools = Array.Empty<string>(),
+            SchemaVersion = 1,
+        };
+        _skillRegistry.Setup(r => r.GetSkillById(agent)).Returns(definition);
+        _skillRegistry.Setup(r => r.LoadLevel2(agent)).Returns("Skill body {{ brand_voice_block }}");
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("system prompt");
         _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("task prompt");
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs
index e23361f..60fc646 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs
@@ -3,6 +3,7 @@ using Microsoft.Extensions.Logging;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Common.Models.Skills;
 using PersonalBrandAssistant.Domain.Enums;
 using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
@@ -10,15 +11,18 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
 
 public class RepurposeAgentCapabilityTests
 {
+    private readonly Mock<ISkillRegistry> _skillRegistry;
     private readonly Mock<IPromptTemplateService> _promptService;
     private readonly Mock<ISidecarClient> _sidecarClient;
     private readonly RepurposeAgentCapability _capability;
 
     public RepurposeAgentCapabilityTests()
     {
+        _skillRegistry = new Mock<ISkillRegistry>();
         _promptService = new Mock<IPromptTemplateService>();
         _sidecarClient = new Mock<ISidecarClient>();
         _capability = new RepurposeAgentCapability(
+            _skillRegistry.Object,
             new Mock<ILogger<RepurposeAgentCapability>>().Object);
     }
 
@@ -61,7 +65,9 @@ public class RepurposeAgentCapabilityTests
         var context = CreateContext();
         await _capability.ExecuteAsync(context, CancellationToken.None);
 
-        _promptService.Verify(p => p.RenderAsync("repurpose", "system", It.IsAny<Dictionary<string, object>>()), Times.Once);
+        _skillRegistry.Verify(r => r.GetSkillById("repurpose"), Times.Once);
+        _skillRegistry.Verify(r => r.LoadLevel2("repurpose"), Times.Once);
+        _promptService.Verify(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
         _promptService.Verify(p => p.RenderAsync("repurpose", "blog-to-thread", It.IsAny<Dictionary<string, object>>()), Times.Once);
     }
 
@@ -82,12 +88,20 @@ public class RepurposeAgentCapabilityTests
     public async Task ExecuteAsync_PassesSourceContentInVariables()
     {
         Dictionary<string, object>? capturedVars = null;
+        var definition = new SkillDefinition
+        {
+            Id = "repurpose", Name = "repurpose", Description = "test",
+            Category = "test", SkillType = "test",
+            Tags = Array.Empty<string>(), AllowedTools = Array.Empty<string>(),
+            SchemaVersion = 1,
+        };
+        _skillRegistry.Setup(r => r.GetSkillById("repurpose")).Returns(definition);
+        _skillRegistry.Setup(r => r.LoadLevel2("repurpose")).Returns("System body");
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("system prompt");
         _promptService.Setup(p => p.RenderAsync("repurpose", "blog-to-thread", It.IsAny<Dictionary<string, object>>()))
             .Callback<string, string, Dictionary<string, object>>((_, _, v) => capturedVars = v)
             .ReturnsAsync("task prompt");
-        _promptService.Setup(p => p.RenderAsync("repurpose", "system", It.IsAny<Dictionary<string, object>>()))
-            .ReturnsAsync("system prompt");
-
         SetupSidecarResponse("Repurposed content.");
 
         var context = CreateContext();
@@ -99,7 +113,16 @@ public class RepurposeAgentCapabilityTests
 
     private void SetupPrompts(string agent, string template)
     {
-        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
+        var definition = new SkillDefinition
+        {
+            Id = agent, Name = agent, Description = "test",
+            Category = "test", SkillType = "test",
+            Tags = Array.Empty<string>(), AllowedTools = Array.Empty<string>(),
+            SchemaVersion = 1,
+        };
+        _skillRegistry.Setup(r => r.GetSkillById(agent)).Returns(definition);
+        _skillRegistry.Setup(r => r.LoadLevel2(agent)).Returns("Skill body {{ brand_voice_block }}");
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("system prompt");
         _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("task prompt");
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs
index 7921634..6d84383 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs
@@ -3,6 +3,7 @@ using Microsoft.Extensions.Logging;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Common.Models.Skills;
 using PersonalBrandAssistant.Domain.Enums;
 using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
@@ -10,15 +11,18 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
 
 public class SocialAgentCapabilityTests
 {
+    private readonly Mock<ISkillRegistry> _skillRegistry;
     private readonly Mock<IPromptTemplateService> _promptService;
     private readonly Mock<ISidecarClient> _sidecarClient;
     private readonly SocialAgentCapability _capability;
 
     public SocialAgentCapabilityTests()
     {
+        _skillRegistry = new Mock<ISkillRegistry>();
         _promptService = new Mock<IPromptTemplateService>();
         _sidecarClient = new Mock<ISidecarClient>();
         _capability = new SocialAgentCapability(
+            _skillRegistry.Object,
             new Mock<ILogger<SocialAgentCapability>>().Object);
     }
 
@@ -53,7 +57,9 @@ public class SocialAgentCapabilityTests
         var context = CreateContext();
         await _capability.ExecuteAsync(context, CancellationToken.None);
 
-        _promptService.Verify(p => p.RenderAsync("social", "system", It.IsAny<Dictionary<string, object>>()), Times.Once);
+        _skillRegistry.Verify(r => r.GetSkillById("social"), Times.Once);
+        _skillRegistry.Verify(r => r.LoadLevel2("social"), Times.Once);
+        _promptService.Verify(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
         _promptService.Verify(p => p.RenderAsync("social", "post", It.IsAny<Dictionary<string, object>>()), Times.Once);
     }
 
@@ -97,7 +103,16 @@ public class SocialAgentCapabilityTests
 
     private void SetupPrompts(string agent, string template)
     {
-        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
+        var definition = new SkillDefinition
+        {
+            Id = agent, Name = agent, Description = "test",
+            Category = "test", SkillType = "test",
+            Tags = Array.Empty<string>(), AllowedTools = Array.Empty<string>(),
+            SchemaVersion = 1,
+        };
+        _skillRegistry.Setup(r => r.GetSkillById(agent)).Returns(definition);
+        _skillRegistry.Setup(r => r.LoadLevel2(agent)).Returns("Skill body {{ brand_voice_block }}");
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("system prompt");
         _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("task prompt");
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs
index d494e87..75220e3 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs
@@ -3,6 +3,7 @@ using Microsoft.Extensions.Logging;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Common.Models.Skills;
 using PersonalBrandAssistant.Domain.Enums;
 using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
 
@@ -10,15 +11,18 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
 
 public class WriterAgentCapabilityTests
 {
+    private readonly Mock<ISkillRegistry> _skillRegistry;
     private readonly Mock<IPromptTemplateService> _promptService;
     private readonly Mock<ISidecarClient> _sidecarClient;
     private readonly WriterAgentCapability _capability;
 
     public WriterAgentCapabilityTests()
     {
+        _skillRegistry = new Mock<ISkillRegistry>();
         _promptService = new Mock<IPromptTemplateService>();
         _sidecarClient = new Mock<ISidecarClient>();
         _capability = new WriterAgentCapability(
+            _skillRegistry.Object,
             new Mock<ILogger<WriterAgentCapability>>().Object);
     }
 
@@ -47,28 +51,22 @@ public class WriterAgentCapabilityTests
     [Fact]
     public async Task ExecuteAsync_LoadsSystemAndTaskTemplates()
     {
-        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
-            .ReturnsAsync("system prompt");
-        _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
-            .ReturnsAsync("task prompt");
-
+        SetupPrompts("writer", "blog-post");
         SetupSidecarResponse("# My Blog Post\n\nThis is the body content.");
 
         var context = CreateContext();
         await _capability.ExecuteAsync(context, CancellationToken.None);
 
-        _promptService.Verify(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()), Times.Once);
+        _skillRegistry.Verify(r => r.GetSkillById("writer"), Times.Once);
+        _skillRegistry.Verify(r => r.LoadLevel2("writer"), Times.Once);
+        _promptService.Verify(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Once);
         _promptService.Verify(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()), Times.Once);
     }
 
     [Fact]
     public async Task ExecuteAsync_UsesTemplateFromParameters()
     {
-        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
-            .ReturnsAsync("system prompt");
-        _promptService.Setup(p => p.RenderAsync("writer", "article", It.IsAny<Dictionary<string, object>>()))
-            .ReturnsAsync("article prompt");
-
+        SetupPrompts("writer", "article");
         SetupSidecarResponse("# Article Title\n\nArticle body.");
 
         var context = CreateContext(new Dictionary<string, string> { ["template"] = "article" });
@@ -119,12 +117,12 @@ public class WriterAgentCapabilityTests
     public async Task ExecuteAsync_InjectsBrandProfileIntoVariables()
     {
         Dictionary<string, object>? capturedVars = null;
-        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
-            .Callback<string, string, Dictionary<string, object>>((_, _, v) => capturedVars = v)
+        SetupSkillRegistry("writer");
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
+            .Callback<string, Dictionary<string, object>>((_, v) => capturedVars = v)
             .ReturnsAsync("system prompt");
         _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("task prompt");
-
         SetupSidecarResponse("# Title\n\nBody");
 
         var context = CreateContext();
@@ -158,12 +156,12 @@ public class WriterAgentCapabilityTests
     public async Task ExecuteAsync_NamespacesParametersUnderTaskKey()
     {
         Dictionary<string, object>? capturedVars = null;
-        _promptService.Setup(p => p.RenderAsync("writer", "system", It.IsAny<Dictionary<string, object>>()))
-            .Callback<string, string, Dictionary<string, object>>((_, _, v) => capturedVars = v)
+        SetupSkillRegistry("writer");
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
+            .Callback<string, Dictionary<string, object>>((_, v) => capturedVars = v)
             .ReturnsAsync("system prompt");
         _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("task prompt");
-
         SetupSidecarResponse("# Title\n\nBody");
 
         var context = CreateContext(new Dictionary<string, string> { ["topic"] = "AI" });
@@ -175,9 +173,23 @@ public class WriterAgentCapabilityTests
         Assert.Equal("AI", taskParams["topic"]);
     }
 
+    private void SetupSkillRegistry(string skillId)
+    {
+        var definition = new SkillDefinition
+        {
+            Id = skillId, Name = skillId, Description = "test",
+            Category = "test", SkillType = "test",
+            Tags = Array.Empty<string>(), AllowedTools = Array.Empty<string>(),
+            SchemaVersion = 1,
+        };
+        _skillRegistry.Setup(r => r.GetSkillById(skillId)).Returns(definition);
+        _skillRegistry.Setup(r => r.LoadLevel2(skillId)).Returns("Skill body {{ brand_voice_block }}");
+    }
+
     private void SetupPrompts(string agent, string template)
     {
-        _promptService.Setup(p => p.RenderAsync(agent, "system", It.IsAny<Dictionary<string, object>>()))
+        SetupSkillRegistry(agent);
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("system prompt");
         _promptService.Setup(p => p.RenderAsync(agent, template, It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("task prompt");
