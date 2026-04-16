using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Application.Common.Models.Skills;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Agents;
using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
using PersonalBrandAssistant.Infrastructure.Services;
using PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

/// <summary>
/// Integration tests verifying AgentCapabilityBase + ObservabilityMiddleware emit correct OTel spans.
/// Uses real ObservabilityMiddleware backed by a mock inner ISidecarClient.
/// </summary>
[Collection("ObservabilityTests")]
public class ObservabilityTelemetryIntegrationTests : IDisposable
{
    private readonly Mock<ISidecarClient> _innerClient = new();
    private readonly Mock<ISkillRegistry> _skillRegistry = new();
    private readonly Mock<IPromptTemplateService> _promptService = new();
    private readonly ObservabilityMiddleware _middleware;
    private readonly ConcurrentBag<Activity> _stoppedActivities = [];
    private readonly ActivityListener _listener;

    public ObservabilityTelemetryIntegrationTests()
    {
        _middleware = new ObservabilityMiddleware(_innerClient.Object);

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgentTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => _stoppedActivities.Add(a)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    // --- Span emission ---

    [Fact]
    public async Task AgentCapabilityExecution_EmitsAgentExecuteSpan()
    {
        SetupFullPipeline("writer");

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Contains(_stoppedActivities, a => a.OperationName.StartsWith("agent."));
    }

    [Fact]
    public async Task AgentCapabilityExecution_SpanHasCapabilityTypeAttribute()
    {
        SetupFullPipeline("writer");

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        var agentSpan = _stoppedActivities.Single(a => a.OperationName.StartsWith("agent."));
        var tags = agentSpan.TagObjects.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.True(tags.ContainsKey("capability_type"));
        Assert.Equal("Writer", tags["capability_type"]?.ToString());
    }

    [Fact]
    public async Task AgentCapabilityExecution_SpanHasSkillIdAttribute()
    {
        SetupFullPipeline("writer");

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        var agentSpan = _stoppedActivities.Single(a => a.OperationName.StartsWith("agent."));
        var tags = agentSpan.TagObjects.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.True(tags.ContainsKey("skill_id"));
        Assert.Equal("writer", tags["skill_id"]?.ToString());
    }

    [Fact]
    public async Task AgentCapabilityExecution_SpanHasCostUsdAttribute()
    {
        SetupFullPipeline("writer", costUsd: 0.01m);

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        var agentSpan = _stoppedActivities.Single(a => a.OperationName.StartsWith("agent."));
        var tags = agentSpan.TagObjects.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.True(tags.ContainsKey("cost_usd"));
        Assert.Equal(0.01m, tags["cost_usd"]);
    }

    // --- Nested spans ---

    [Fact]
    public async Task AgentCapabilityExecution_SidecarSpanIsChildOfAgentSpan()
    {
        SetupFullPipeline("writer");

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        var agentSpan = _stoppedActivities.Single(a => a.OperationName.StartsWith("agent."));
        var sidecarSpan = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");

        Assert.Equal(agentSpan.Id, sidecarSpan.ParentId);
    }

    [Fact]
    public async Task SkillLoad_EmitsSkillLoadSpan_OnlyOnFirstAccess()
    {
        var registry = new SpanEmittingTestRegistry();
        SetupPromptService("writer");
        SetupInnerClient(0.005m);

        var capability = new WriterAgentCapability(registry, new Mock<ILogger<WriterAgentCapability>>().Object);

        // First call — Lazy factory runs, one skill.load span emitted
        await capability.ExecuteAsync(CreateContext(), CancellationToken.None);
        var afterFirst = _stoppedActivities.Count(a => a.OperationName == "skill.load");

        // Second call — Lazy factory cached, no new skill.load span
        await capability.ExecuteAsync(CreateContext(), CancellationToken.None);
        var afterSecond = _stoppedActivities.Count(a => a.OperationName == "skill.load");

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, afterSecond); // count unchanged — factory did not run again
    }

    [Fact]
    public async Task SkillLoad_CacheHit_EmitsNoSpan()
    {
        var registry = new SpanEmittingTestRegistry();
        SetupPromptService("writer");
        SetupInnerClient();

        var capability = new WriterAgentCapability(registry, new Mock<ILogger<WriterAgentCapability>>().Object);

        // First call — factory runs, span emitted
        await capability.ExecuteAsync(CreateContext(), CancellationToken.None);
        var afterFirst = _stoppedActivities.Count(a => a.OperationName == "skill.load");

        // Second call — factory does NOT run (cached), no new skill.load span
        await capability.ExecuteAsync(CreateContext(), CancellationToken.None);
        var afterSecond = _stoppedActivities.Count(a => a.OperationName == "skill.load");

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, afterSecond); // still 1 — no second span emitted
    }

    // --- Privacy ---

    [Fact]
    public async Task AllSpans_ContainNoPromptText()
    {
        const string knownPrompt = "TELEMETRY_PRIVACY_TEST_PROMPT";
        SetupFullPipeline("writer", systemPrompt: knownPrompt);

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        var snapshot = _stoppedActivities.ToList(); // snapshot before iteration — ConcurrentBag is safe to enumerate
        foreach (var activity in snapshot)
        {
            var allValues = activity.TagObjects
                .Select(kv => kv.Value?.ToString() ?? "")
                .Concat(activity.Tags.Select(kv => kv.Value ?? ""))
                .ToList();

            Assert.DoesNotContain(allValues, v => v.Contains(knownPrompt));
        }
    }

    [Fact]
    public async Task AllSpans_ContainNoResponseText()
    {
        const string knownResponse = "TELEMETRY_PRIVACY_TEST_RESPONSE";
        SetupFullPipeline("writer", responseText: knownResponse);

        await CreateCapability().ExecuteAsync(CreateContext(), CancellationToken.None);

        var snapshot = _stoppedActivities.ToList();
        foreach (var activity in snapshot)
        {
            var allValues = activity.TagObjects
                .Select(kv => kv.Value?.ToString() ?? "")
                .Concat(activity.Tags.Select(kv => kv.Value ?? ""))
                .ToList();

            Assert.DoesNotContain(allValues, v => v.Contains(knownResponse));
        }
    }

    // --- Setup helpers ---

    private void SetupFullPipeline(
        string skillId,
        string systemPrompt = "system prompt",
        string responseText = "Generated content",
        decimal costUsd = 0m)
    {
        var definition = new SkillDefinition
        {
            Id = skillId, Name = skillId, Description = "test",
            Category = "test", SkillType = "test",
            Tags = [], AllowedTools = [], SchemaVersion = 1
        };
        _skillRegistry.Setup(r => r.GetSkillById(skillId)).Returns(definition);
        _skillRegistry.Setup(r => r.LoadLevel2(skillId)).Returns("Skill body {{ brand_voice_block }}");

        SetupPromptService(skillId, systemPrompt);
        SetupInnerClient(costUsd, responseText);
    }

    private void SetupPromptService(string agentName, string systemPrompt = "system prompt")
    {
        _promptService.Setup(p => p.RenderRawAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(systemPrompt);
        _promptService.Setup(p => p.RenderAsync(agentName, It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync("task prompt");
    }

    private void SetupInnerClient(decimal cost = 0m, string text = "Generated content")
    {
        _innerClient.Setup(c => c.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(FakeEvents(text, cost));
    }

    private AgentContext CreateContext() => new()
    {
        ExecutionId = Guid.NewGuid(),
        BrandProfile = TestBrandProfile.Create(),
        PromptService = _promptService.Object,
        SidecarClient = _middleware,
        Parameters = new Dictionary<string, string>(),
    };

    private WriterAgentCapability CreateCapability() =>
        new(_skillRegistry.Object, new Mock<ILogger<WriterAgentCapability>>().Object);

    private static async IAsyncEnumerable<SidecarEvent> FakeEvents(
        string text, decimal cost,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ChatEvent("assistant", text, null, null);
        yield return new TaskCompleteEvent("mock-session", 100, 50, Cost: cost);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Test double for ISkillRegistry that emits a skill.load span on the first LoadLevel2 call,
    /// mirroring the lazy-factory pattern in the real SkillRegistry.
    /// </summary>
    private sealed class SpanEmittingTestRegistry : ISkillRegistry
    {
        private readonly ConcurrentDictionary<string, Lazy<string>> _cache = new();

        public SkillDefinition? GetSkillById(string id) => new()
        {
            Id = id, Name = id, Description = "test",
            Category = "test", SkillType = "test",
            Tags = [], AllowedTools = [], SchemaVersion = 1
        };

        public IReadOnlyCollection<SkillDefinition> GetAllSkills() => [];

        public string LoadLevel2(string skillId) =>
            _cache.GetOrAdd(
                skillId,
                key => new Lazy<string>(() =>
                {
                    using var activity = AgentTelemetry.Source.StartActivity("skill.load");
                    activity?.SetTag("skill_id", key);
                    return $"Skill body for {key} {{{{ brand_voice_block }}}}";
                }, LazyThreadSafetyMode.ExecutionAndPublication)
            ).Value;
    }
}
