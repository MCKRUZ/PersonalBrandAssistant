diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/AgentTelemetry.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/AgentTelemetry.cs
new file mode 100644
index 0000000..e7deef6
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/AgentTelemetry.cs
@@ -0,0 +1,9 @@
+using System.Diagnostics;
+
+namespace PersonalBrandAssistant.Infrastructure.Agents;
+
+public static class AgentTelemetry
+{
+    public const string SourceName = "PersonalBrandAssistant.Agents";
+    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
index c3c16e6..3857eb7 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
@@ -1,3 +1,4 @@
+using System.Diagnostics;
 using System.Text;
 using Microsoft.Extensions.Logging;
 using PersonalBrandAssistant.Application.Common.Errors;
@@ -27,6 +28,10 @@ public abstract class AgentCapabilityBase : IAgentCapability
 
     public async Task<Result<AgentOutput>> ExecuteAsync(AgentContext context, CancellationToken ct)
     {
+        using var activity = AgentTelemetry.Source.StartActivity($"agent.{AgentName}.execute");
+        activity?.SetTag("capability_type", Type.ToString());
+        activity?.SetTag("skill_id", SkillName);
+
         try
         {
             var templateName = context.Parameters.GetValueOrDefault("template", DefaultTemplate);
@@ -84,14 +89,17 @@ public abstract class AgentCapabilityBase : IAgentCapability
                     $"{Type} capability received empty response from sidecar");
             }
 
+            activity?.SetTag("cost_usd", cost);
             return BuildOutput(responseText, inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens, cost, fileChanges);
         }
         catch (OperationCanceledException)
         {
+            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
             throw;
         }
         catch (Exception ex)
         {
+            activity?.SetStatus(ActivityStatusCode.Error, "agent execution failed");
             _logger.LogError(ex, "{Capability} failed during execution", Type);
             return Result<AgentOutput>.Failure(ErrorCode.InternalError,
                 $"{Type} capability encountered an unexpected error");
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ObservabilityMiddleware.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ObservabilityMiddleware.cs
new file mode 100644
index 0000000..eb797a3
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ObservabilityMiddleware.cs
@@ -0,0 +1,89 @@
+using System.Diagnostics;
+using System.Runtime.CompilerServices;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.Agents;
+
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+/// <summary>
+/// ISidecarClient decorator that wraps every SendTaskAsync call in a sidecar.send_task OTel span.
+/// Sets token/cost attributes from TaskCompleteEvent. Never emits prompt or response text in span attributes.
+/// Disposes Activity in try/finally to handle early-break, cancellation, and exceptions.
+/// </summary>
+public sealed class ObservabilityMiddleware : ISidecarClient
+{
+    private readonly ISidecarClient _inner;
+    private readonly ILogger<ObservabilityMiddleware> _logger;
+
+    public ObservabilityMiddleware(ISidecarClient inner, ILogger<ObservabilityMiddleware> logger)
+    {
+        _inner = inner;
+        _logger = logger;
+    }
+
+    public bool IsConnected => _inner.IsConnected;
+
+    public Task<SidecarSession> ConnectAsync(CancellationToken ct) => _inner.ConnectAsync(ct);
+
+    public Task<SidecarSession> NewSessionAsync(CancellationToken ct) => _inner.NewSessionAsync(ct);
+
+    public Task AbortAsync(string? sessionId, CancellationToken ct) => _inner.AbortAsync(sessionId, ct);
+
+    public async IAsyncEnumerable<SidecarEvent> SendTaskAsync(
+        string task,
+        string? systemPrompt,
+        string? sessionId,
+        string? modelId,
+        [EnumeratorCancellation] CancellationToken ct)
+    {
+        var activity = AgentTelemetry.Source.StartActivity("sidecar.send_task");
+        activity?.SetTag("has_system_prompt", systemPrompt != null);
+
+        // Manual enumerator pattern: MoveNextAsync (can throw) is inside try/catch,
+        // yield return is in the outer try/finally (no catch), which is legal in C#.
+        var enumerator = _inner.SendTaskAsync(task, systemPrompt, sessionId, modelId, ct)
+            .GetAsyncEnumerator(ct);
+        try
+        {
+            while (true)
+            {
+                bool hasNext;
+                try
+                {
+                    hasNext = await enumerator.MoveNextAsync();
+                }
+                catch (OperationCanceledException)
+                {
+                    activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
+                    throw;
+                }
+                catch (Exception)
+                {
+                    activity?.SetStatus(ActivityStatusCode.Error, "sidecar error");
+                    throw;
+                }
+
+                if (!hasNext)
+                    break;
+
+                var evt = enumerator.Current;
+                if (evt is TaskCompleteEvent complete)
+                {
+                    activity?.SetTag("gen_ai.usage.input_tokens", complete.InputTokens);
+                    activity?.SetTag("gen_ai.usage.output_tokens", complete.OutputTokens);
+                    activity?.SetTag("gen_ai.usage.cache_read_tokens", complete.CacheReadTokens);
+                    activity?.SetTag("cost_usd", complete.Cost);
+                }
+
+                yield return evt; // outside any try/catch — legal
+            }
+        }
+        finally
+        {
+            await enumerator.DisposeAsync();
+            activity?.Dispose();
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Skills/SkillRegistry.cs b/src/PersonalBrandAssistant.Infrastructure/Skills/SkillRegistry.cs
index ebc57ee..ff2bc5e 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Skills/SkillRegistry.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Skills/SkillRegistry.cs
@@ -6,6 +6,7 @@ using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models.Skills;
+using PersonalBrandAssistant.Infrastructure.Agents;
 
 namespace PersonalBrandAssistant.Infrastructure.Skills;
 
@@ -55,8 +56,13 @@ public sealed class SkillRegistry : ISkillRegistry
 
         return _level2Cache.GetOrAdd(
             skillId,
-            _ => new Lazy<string>(
-                () => ExtractLevel2Body(entry.RawContent, entry.FilePath),
+            key => new Lazy<string>(
+                () =>
+                {
+                    using var activity = AgentTelemetry.Source.StartActivity("skill.load");
+                    activity?.SetTag("skill_id", key);
+                    return ExtractLevel2Body(entry.RawContent, entry.FilePath);
+                },
                 LazyThreadSafetyMode.ExecutionAndPublication)
         ).Value;
     }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ObservabilityMiddlewareTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ObservabilityMiddlewareTests.cs
new file mode 100644
index 0000000..eaa13d9
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ObservabilityMiddlewareTests.cs
@@ -0,0 +1,234 @@
+using System.Collections.Concurrent;
+using System.Diagnostics;
+using System.Runtime.CompilerServices;
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.Agents;
+using PersonalBrandAssistant.Infrastructure.Services;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
+
+[Collection("ObservabilityTests")]
+public class ObservabilityMiddlewareTests : IDisposable
+{
+    private readonly Mock<ISidecarClient> _inner = new();
+    private readonly ObservabilityMiddleware _middleware;
+    private readonly ConcurrentBag<Activity> _stoppedActivities = [];
+    private readonly ActivityListener _listener;
+
+    public ObservabilityMiddlewareTests()
+    {
+        _middleware = new ObservabilityMiddleware(_inner.Object, NullLogger<ObservabilityMiddleware>.Instance);
+
+        _listener = new ActivityListener
+        {
+            ShouldListenTo = source => source.Name == AgentTelemetry.SourceName,
+            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
+            ActivityStopped = a => _stoppedActivities.Add(a)
+        };
+        ActivitySource.AddActivityListener(_listener);
+    }
+
+    public void Dispose() => _listener.Dispose();
+
+    // --- Delegation ---
+
+    [Fact]
+    public async Task SendTaskAsync_DelegatesToInnerClient()
+    {
+        SetupInner([new TaskCompleteEvent("s", 1, 1)]);
+
+        await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, CancellationToken.None)) { }
+
+        _inner.Verify(c => c.SendTaskAsync("task", null, null, null, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task ConnectAsync_DelegatesToInnerClient()
+    {
+        var session = new SidecarSession("s1", DateTimeOffset.UtcNow);
+        _inner.Setup(c => c.ConnectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(session);
+
+        var result = await _middleware.ConnectAsync(CancellationToken.None);
+
+        Assert.Equal(session, result);
+        _inner.Verify(c => c.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public void IsConnected_DelegatesToInnerClient()
+    {
+        _inner.SetupGet(c => c.IsConnected).Returns(true);
+
+        Assert.True(_middleware.IsConnected);
+    }
+
+    // --- Span creation ---
+
+    [Fact]
+    public async Task SendTaskAsync_StartsActivity_WithCorrectSpanName()
+    {
+        SetupInner([new TaskCompleteEvent("s", 1, 1)]);
+
+        await foreach (var _ in _middleware.SendTaskAsync("task", "sys", null, null, CancellationToken.None)) { }
+
+        Assert.Contains(_stoppedActivities, a => a.OperationName == "sidecar.send_task");
+    }
+
+    [Fact]
+    public async Task SendTaskAsync_SetsTokenAttributes_FromTaskCompleteEvent()
+    {
+        SetupInner([new TaskCompleteEvent("s", InputTokens: 100, OutputTokens: 50, CacheReadTokens: 25, Cost: 0.005m)]);
+
+        await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, CancellationToken.None)) { }
+
+        var activity = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
+        var tags = activity.TagObjects.ToDictionary(kv => kv.Key, kv => kv.Value);
+        Assert.Equal(100, tags["gen_ai.usage.input_tokens"]);
+        Assert.Equal(50, tags["gen_ai.usage.output_tokens"]);
+        Assert.Equal(25, tags["gen_ai.usage.cache_read_tokens"]);
+        Assert.Equal(0.005m, tags["cost_usd"]);
+    }
+
+    [Theory]
+    [InlineData("system prompt text", true)]
+    [InlineData(null, false)]
+    public async Task SendTaskAsync_SetsHasSystemPromptAttribute(string? prompt, bool expected)
+    {
+        SetupInner([new TaskCompleteEvent("s", 1, 1)]);
+
+        await foreach (var _ in _middleware.SendTaskAsync("task", prompt, null, null, CancellationToken.None)) { }
+
+        var activity = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
+        var tag = activity.TagObjects.Single(kv => kv.Key == "has_system_prompt").Value;
+        Assert.Equal(expected, tag);
+    }
+
+    // --- Span lifecycle ---
+
+    [Fact]
+    public async Task SendTaskAsync_ConsumerBreaksEarly_SpanIsClosed()
+    {
+        SetupInner([new ChatEvent("assistant", "hello", null, null), new TaskCompleteEvent("s", 1, 1)]);
+
+        await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, CancellationToken.None))
+        {
+            break; // consume only first event then break
+        }
+
+        await Task.Yield(); // allow state machine finalization
+        Assert.Contains(_stoppedActivities, a => a.OperationName == "sidecar.send_task");
+    }
+
+    [Fact]
+    public async Task SendTaskAsync_CancellationToken_SpanIsClosedWithCancellation()
+    {
+        using var cts = new CancellationTokenSource();
+        _inner.Setup(c => c.SendTaskAsync(It.IsAny<string>(), It.IsAny<string?>(),
+                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .Returns(BlockingUntilCancelledEnumerable(cts.Token));
+
+        cts.Cancel();
+
+        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
+        {
+            await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, cts.Token)) { }
+        });
+
+        Assert.Contains(_stoppedActivities, a => a.OperationName == "sidecar.send_task");
+    }
+
+    [Fact]
+    public async Task SendTaskAsync_InnerThrows_SpanIsClosedWithError()
+    {
+        _inner.Setup(c => c.SendTaskAsync(It.IsAny<string>(), It.IsAny<string?>(),
+                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .Returns(ThrowingEnumerable());
+
+        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
+        {
+            await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, CancellationToken.None)) { }
+        });
+
+        var activity = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
+        Assert.Equal(ActivityStatusCode.Error, activity.Status);
+        Assert.Equal("sidecar error", activity.StatusDescription);
+    }
+
+    // --- Privacy ---
+
+    [Fact]
+    public async Task SendTaskAsync_NoPromptContentInSpanAttributes()
+    {
+        const string secretPrompt = "CONFIDENTIAL_SYSTEM_PROMPT_CONTENT";
+        SetupInner([new TaskCompleteEvent("s", 1, 1)]);
+
+        await foreach (var _ in _middleware.SendTaskAsync("task", secretPrompt, null, null, CancellationToken.None)) { }
+
+        var activity = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
+        var allTagValues = activity.TagObjects
+            .Select(kv => kv.Value?.ToString() ?? "")
+            .Concat(activity.Tags.Select(kv => kv.Value ?? ""))
+            .ToList();
+
+        Assert.DoesNotContain(allTagValues, v => v.Contains(secretPrompt));
+    }
+
+    [Fact]
+    public async Task SendTaskAsync_ErrorStatus_UsesGenericMessage()
+    {
+        const string promptFragment = "SECRET_PROMPT_IN_EXCEPTION";
+        _inner.Setup(c => c.SendTaskAsync(It.IsAny<string>(), It.IsAny<string?>(),
+                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .Returns(ThrowingEnumerable($"Error processing: {promptFragment}"));
+
+        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
+        {
+            await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, CancellationToken.None)) { }
+        });
+
+        var activity = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
+        Assert.Equal(ActivityStatusCode.Error, activity.Status);
+        Assert.DoesNotContain(promptFragment, activity.StatusDescription ?? "");
+        Assert.Equal("sidecar error", activity.StatusDescription);
+    }
+
+    // --- Helpers ---
+
+    private void SetupInner(IEnumerable<SidecarEvent> events) =>
+        _inner.Setup(c => c.SendTaskAsync(It.IsAny<string>(), It.IsAny<string?>(),
+                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
+            .Returns(StaticEnumerable(events));
+
+    private static async IAsyncEnumerable<SidecarEvent> StaticEnumerable(
+        IEnumerable<SidecarEvent> events,
+        [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        foreach (var evt in events)
+        {
+            ct.ThrowIfCancellationRequested();
+            yield return evt;
+        }
+        await Task.CompletedTask;
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> BlockingUntilCancelledEnumerable(
+        [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        await Task.Delay(Timeout.Infinite, ct);
+        yield break;
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> ThrowingEnumerable(
+        string message = "inner error",
+        [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        await Task.CompletedTask;
+        throw new InvalidOperationException(message);
+#pragma warning disable CS0162 // Unreachable code — required for compiler to recognize this as an iterator method
+        yield break;
+#pragma warning restore CS0162
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ObservabilityTelemetryIntegrationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ObservabilityTelemetryIntegrationTests.cs
new file mode 100644
index 0000000..29a5e6f
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ObservabilityTelemetryIntegrationTests.cs
@@ -0,0 +1,286 @@
+using System.Collections.Concurrent;
+using System.Diagnostics;
+using System.Runtime.CompilerServices;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Application.Common.Models.Skills;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Agents;
+using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
+using PersonalBrandAssistant.Infrastructure.Services;
+using PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
+
+/// <summary>
+/// Integration tests verifying AgentCapabilityBase + ObservabilityMiddleware emit correct OTel spans.
+/// Uses real ObservabilityMiddleware backed by a mock inner ISidecarClient.
+/// </summary>
+[Collection("ObservabilityTests")]
+public class ObservabilityTelemetryIntegrationTests : IDisposable
+{
+    private readonly Mock<ISidecarClient> _innerClient = new();
+    private readonly Mock<ISkillRegistry> _skillRegistry = new();
+    private readonly Mock<IPromptTemplateService> _promptService = new();
+    private readonly ObservabilityMiddleware _middleware;
+    private readonly ConcurrentBag<Activity> _stoppedActivities = [];
+    private readonly ActivityListener _listener;
+
+    public ObservabilityTelemetryIntegrationTests()
+    {
+        _middleware = new ObservabilityMiddleware(
+            _innerClient.Object,
+            NullLogger<ObservabilityMiddleware>.Instance);
+
+        _listener = new ActivityListener
+        {
+            ShouldListenTo = source => source.Name == AgentTelemetry.SourceName,
+            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
+            ActivityStopped = a => _stoppedActivities.Add(a)
+        };
+        ActivitySource.AddActivityListener(_listener);
+    }
+
+    public void Dispose() => _listener.Dispose();
+
+    // --- Span emission ---
+
+    [Fact]
+    public async Task AgentCapabilityExecution_EmitsAgentExecuteSpan()
+    {
+        SetupFullPipeline("writer");
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        Assert.Contains(_stoppedActivities, a => a.OperationName.StartsWith("agent."));
+    }
+
+    [Fact]
+    public async Task AgentCapabilityExecution_SpanHasCapabilityTypeAttribute()
+    {
+        SetupFullPipeline("writer");
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        var agentSpan = _stoppedActivities.Single(a => a.OperationName.StartsWith("agent."));
+        var tags = agentSpan.TagObjects.ToDictionary(kv => kv.Key, kv => kv.Value);
+        Assert.True(tags.ContainsKey("capability_type"));
+        Assert.Equal("Writer", tags["capability_type"]?.ToString());
+    }
+
+    [Fact]
+    public async Task AgentCapabilityExecution_SpanHasSkillIdAttribute()
+    {
+        SetupFullPipeline("writer");
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        var agentSpan = _stoppedActivities.Single(a => a.OperationName.StartsWith("agent."));
+        var tags = agentSpan.TagObjects.ToDictionary(kv => kv.Key, kv => kv.Value);
+        Assert.True(tags.ContainsKey("skill_id"));
+        Assert.Equal("writer", tags["skill_id"]?.ToString());
+    }
+
+    [Fact]
+    public async Task AgentCapabilityExecution_SpanHasCostUsdAttribute()
+    {
+        SetupFullPipeline("writer", costUsd: 0.01m);
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        var agentSpan = _stoppedActivities.Single(a => a.OperationName.StartsWith("agent."));
+        var tags = agentSpan.TagObjects.ToDictionary(kv => kv.Key, kv => kv.Value);
+        Assert.True(tags.ContainsKey("cost_usd"));
+        Assert.Equal(0.01m, tags["cost_usd"]);
+    }
+
+    // --- Nested spans ---
+
+    [Fact]
+    public async Task AgentCapabilityExecution_SidecarSpanIsChildOfAgentSpan()
+    {
+        SetupFullPipeline("writer");
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        var agentSpan = _stoppedActivities.Single(a => a.OperationName.StartsWith("agent."));
+        var sidecarSpan = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
+
+        Assert.Equal(agentSpan.Id, sidecarSpan.ParentId);
+    }
+
+    [Fact]
+    public async Task SkillLoad_EmitsSkillLoadSpan_OnlyOnFirstAccess()
+    {
+        var registry = new SpanEmittingTestRegistry();
+        SetupPromptService("writer");
+        SetupInnerClient(0.005m);
+
+        var capability = new WriterAgentCapability(registry, new Mock<ILogger<WriterAgentCapability>>().Object);
+
+        // First call — Lazy factory runs, one skill.load span emitted
+        await capability.ExecuteAsync(CreateContext(), CancellationToken.None);
+        var afterFirst = _stoppedActivities.Count(a => a.OperationName == "skill.load");
+
+        // Second call — Lazy factory cached, no new skill.load span
+        await capability.ExecuteAsync(CreateContext(), CancellationToken.None);
+        var afterSecond = _stoppedActivities.Count(a => a.OperationName == "skill.load");
+
+        Assert.Equal(1, afterFirst);
+        Assert.Equal(1, afterSecond); // count unchanged — factory did not run again
+    }
+
+    [Fact]
+    public async Task SkillLoad_CacheHit_EmitsNoSpan()
+    {
+        var registry = new SpanEmittingTestRegistry();
+        SetupPromptService("writer");
+        SetupInnerClient();
+
+        var capability = new WriterAgentCapability(registry, new Mock<ILogger<WriterAgentCapability>>().Object);
+
+        // First call — factory runs, span emitted
+        await capability.ExecuteAsync(CreateContext(), CancellationToken.None);
+        var afterFirst = _stoppedActivities.Count(a => a.OperationName == "skill.load");
+
+        // Second call — factory does NOT run (cached), no new skill.load span
+        await capability.ExecuteAsync(CreateContext(), CancellationToken.None);
+        var afterSecond = _stoppedActivities.Count(a => a.OperationName == "skill.load");
+
+        Assert.Equal(1, afterFirst);
+        Assert.Equal(1, afterSecond); // still 1 — no second span emitted
+    }
+
+    // --- Privacy ---
+
+    [Fact]
+    public async Task AllSpans_ContainNoPromptText()
+    {
+        const string knownPrompt = "TELEMETRY_PRIVACY_TEST_PROMPT";
+        SetupFullPipeline("writer", systemPrompt: knownPrompt);
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        var snapshot = _stoppedActivities.ToList(); // snapshot before iteration — ConcurrentBag is safe to enumerate
+        foreach (var activity in snapshot)
+        {
+            var allValues = activity.TagObjects
+                .Select(kv => kv.Value?.ToString() ?? "")
+                .Concat(activity.Tags.Select(kv => kv.Value ?? ""))
+                .ToList();
+
+            Assert.DoesNotContain(allValues, v => v.Contains(knownPrompt));
+        }
+    }
+
+    [Fact]
+    public async Task AllSpans_ContainNoResponseText()
+    {
+        const string knownResponse = "TELEMETRY_PRIVACY_TEST_RESPONSE";
+        SetupFullPipeline("writer", responseText: knownResponse);
+
+        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);
+
+        var snapshot = _stoppedActivities.ToList();
+        foreach (var activity in snapshot)
+        {
+            var allValues = activity.TagObjects
+                .Select(kv => kv.Value?.ToString() ?? "")
+                .Concat(activity.Tags.Select(kv => kv.Value ?? ""))
+                .ToList();
+
+            Assert.DoesNotContain(allValues, v => v.Contains(knownResponse));
+        }
+    }
+
+    // --- Setup helpers ---
+
+    private void SetupFullPipeline(
+        string skillId,
+        string systemPrompt = "system prompt",
+        string responseText = "Generated content",
+        decimal costUsd = 0m)
+    {
+        var definition = new SkillDefinition
+        {
+            Id = skillId, Name = skillId, Description = "test",
+            Category = "test", SkillType = "test",
+            Tags = [], AllowedTools = [], SchemaVersion = 1
+        };
+        _skillRegistry.Setup(r => r.GetSkillById(skillId)).Returns(definition);
+        _skillRegistry.Setup(r => r.LoadLevel2(skillId)).Returns("Skill body {{ brand_voice_block }}");
+
+        SetupPromptService(skillId, systemPrompt);
+        SetupInnerClient(costUsd, responseText);
+    }
+
+    private void SetupPromptService(string agentName, string systemPrompt = "system prompt")
+    {
+        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync(systemPrompt);
+        _promptService.Setup(p => p.RenderAsync(agentName, It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
+            .ReturnsAsync("task prompt");
+    }
+
+    private void SetupInnerClient(decimal cost = 0m, string text = "Generated content")
+    {
+        _innerClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(), It.IsAny<string?>(),
+                It.IsAny<string?>(), It.IsAny<string?>(),
+                It.IsAny<CancellationToken>()))
+            .Returns(FakeEvents(text, cost));
+    }
+
+    private AgentContext CreateContext() => new()
+    {
+        ExecutionId = Guid.NewGuid(),
+        BrandProfile = TestBrandProfile.Create(),
+        PromptService = _promptService.Object,
+        SidecarClient = _middleware,
+        Parameters = new Dictionary<string, string>(),
+    };
+
+    private WriterAgentCapability CreateCapability() =>
+        new(_skillRegistry.Object, new Mock<ILogger<WriterAgentCapability>>().Object);
+
+    private static async IAsyncEnumerable<SidecarEvent> FakeEvents(
+        string text, decimal cost,
+        [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        yield return new ChatEvent("assistant", text, null, null);
+        yield return new TaskCompleteEvent("mock-session", 100, 50, Cost: cost);
+        await Task.CompletedTask;
+    }
+
+    /// <summary>
+    /// Test double for ISkillRegistry that emits a skill.load span on the first LoadLevel2 call,
+    /// mirroring the lazy-factory pattern in the real SkillRegistry.
+    /// </summary>
+    private sealed class SpanEmittingTestRegistry : ISkillRegistry
+    {
+        private readonly ConcurrentDictionary<string, Lazy<string>> _cache = new();
+
+        public SkillDefinition? GetSkillById(string id) => new()
+        {
+            Id = id, Name = id, Description = "test",
+            Category = "test", SkillType = "test",
+            Tags = [], AllowedTools = [], SchemaVersion = 1
+        };
+
+        public IReadOnlyCollection<SkillDefinition> GetAllSkills() => [];
+
+        public string LoadLevel2(string skillId) =>
+            _cache.GetOrAdd(
+                skillId,
+                key => new Lazy<string>(() =>
+                {
+                    using var activity = AgentTelemetry.Source.StartActivity("skill.load");
+                    activity?.SetTag("skill_id", key);
+                    return $"Skill body for {key} {{{{ brand_voice_block }}}}";
+                }, LazyThreadSafetyMode.ExecutionAndPublication)
+            ).Value;
+    }
+}
