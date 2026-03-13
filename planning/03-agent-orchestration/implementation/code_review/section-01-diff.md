diff --git a/src/PersonalBrandAssistant.Domain/Entities/AgentExecution.cs b/src/PersonalBrandAssistant.Domain/Entities/AgentExecution.cs
new file mode 100644
index 0000000..03b17f2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/AgentExecution.cs
@@ -0,0 +1,98 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class AgentExecution : AuditableEntityBase
+{
+    private AgentExecution() { }
+
+    public Guid? ContentId { get; private init; }
+    public AgentCapabilityType AgentType { get; private init; }
+    public AgentExecutionStatus Status { get; private set; } = AgentExecutionStatus.Pending;
+    public ModelTier ModelUsed { get; private init; }
+    public string? ModelId { get; private set; }
+    public int InputTokens { get; private set; }
+    public int OutputTokens { get; private set; }
+    public int CacheReadTokens { get; private set; }
+    public int CacheCreationTokens { get; private set; }
+    public decimal Cost { get; private set; }
+    public DateTimeOffset StartedAt { get; private init; }
+    public DateTimeOffset? CompletedAt { get; private set; }
+    public TimeSpan? Duration { get; private set; }
+    public string? Error { get; private set; }
+    public string? OutputSummary { get; private set; }
+
+    public static AgentExecution Create(
+        AgentCapabilityType agentType,
+        ModelTier modelTier,
+        Guid? contentId = null) =>
+        new()
+        {
+            AgentType = agentType,
+            ModelUsed = modelTier,
+            ContentId = contentId,
+            Status = AgentExecutionStatus.Pending,
+            StartedAt = DateTimeOffset.UtcNow,
+        };
+
+    public void MarkRunning()
+    {
+        if (Status != AgentExecutionStatus.Pending)
+            throw new InvalidOperationException(
+                $"Cannot mark as running from {Status}. Must be Pending.");
+
+        Status = AgentExecutionStatus.Running;
+    }
+
+    public void Complete(string? outputSummary = null)
+    {
+        if (Status != AgentExecutionStatus.Running)
+            throw new InvalidOperationException(
+                $"Cannot complete from {Status}. Must be Running.");
+
+        Status = AgentExecutionStatus.Completed;
+        CompletedAt = DateTimeOffset.UtcNow;
+        Duration = CompletedAt - StartedAt;
+        OutputSummary = outputSummary;
+    }
+
+    public void Fail(string error)
+    {
+        if (Status is not (AgentExecutionStatus.Running or AgentExecutionStatus.Pending))
+            throw new InvalidOperationException(
+                $"Cannot fail from {Status}. Must be Running or Pending.");
+
+        Status = AgentExecutionStatus.Failed;
+        Error = error;
+        CompletedAt = DateTimeOffset.UtcNow;
+        Duration = CompletedAt - StartedAt;
+    }
+
+    public void Cancel()
+    {
+        if (Status is AgentExecutionStatus.Completed or AgentExecutionStatus.Failed)
+            throw new InvalidOperationException(
+                $"Cannot cancel from {Status}. Already in terminal state.");
+
+        Status = AgentExecutionStatus.Cancelled;
+        CompletedAt = DateTimeOffset.UtcNow;
+        Duration = CompletedAt - StartedAt;
+    }
+
+    public void RecordUsage(
+        string modelId,
+        int inputTokens,
+        int outputTokens,
+        int cacheReadTokens,
+        int cacheCreationTokens,
+        decimal cost)
+    {
+        ModelId = modelId;
+        InputTokens = inputTokens;
+        OutputTokens = outputTokens;
+        CacheReadTokens = cacheReadTokens;
+        CacheCreationTokens = cacheCreationTokens;
+        Cost = cost;
+    }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/AgentExecutionLog.cs b/src/PersonalBrandAssistant.Domain/Entities/AgentExecutionLog.cs
new file mode 100644
index 0000000..0cbd2de
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/AgentExecutionLog.cs
@@ -0,0 +1,33 @@
+using PersonalBrandAssistant.Domain.Common;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class AgentExecutionLog : EntityBase
+{
+    private const int MaxContentLength = 2000;
+
+    private AgentExecutionLog() { }
+
+    public Guid AgentExecutionId { get; private init; }
+    public int StepNumber { get; private init; }
+    public string StepType { get; private init; } = string.Empty;
+    public string? Content { get; private init; }
+    public int TokensUsed { get; private init; }
+    public DateTimeOffset Timestamp { get; private init; }
+
+    public static AgentExecutionLog Create(
+        Guid agentExecutionId,
+        int stepNumber,
+        string stepType,
+        string? content,
+        int tokensUsed) =>
+        new()
+        {
+            AgentExecutionId = agentExecutionId,
+            StepNumber = stepNumber,
+            StepType = stepType,
+            Content = content?.Length > MaxContentLength ? content[..MaxContentLength] : content,
+            TokensUsed = tokensUsed,
+            Timestamp = DateTimeOffset.UtcNow,
+        };
+}
diff --git a/src/PersonalBrandAssistant.Domain/Enums/AgentCapabilityType.cs b/src/PersonalBrandAssistant.Domain/Enums/AgentCapabilityType.cs
new file mode 100644
index 0000000..4c388d8
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/AgentCapabilityType.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum AgentCapabilityType { Writer, Social, Repurpose, Engagement, Analytics }
diff --git a/src/PersonalBrandAssistant.Domain/Enums/AgentExecutionStatus.cs b/src/PersonalBrandAssistant.Domain/Enums/AgentExecutionStatus.cs
new file mode 100644
index 0000000..a8b4a71
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/AgentExecutionStatus.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum AgentExecutionStatus { Pending, Running, Completed, Failed, Cancelled }
diff --git a/src/PersonalBrandAssistant.Domain/Enums/ModelTier.cs b/src/PersonalBrandAssistant.Domain/Enums/ModelTier.cs
new file mode 100644
index 0000000..a861b68
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/ModelTier.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum ModelTier { Fast, Standard, Advanced }
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/AgentExecutionLogTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/AgentExecutionLogTests.cs
new file mode 100644
index 0000000..d01ee7e
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/AgentExecutionLogTests.cs
@@ -0,0 +1,71 @@
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class AgentExecutionLogTests
+{
+    private static readonly Guid TestExecutionId = Guid.NewGuid();
+
+    [Fact]
+    public void Create_SetsIdAsNonEmptyGuid()
+    {
+        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", "test content", 100);
+        Assert.NotEqual(Guid.Empty, log.Id);
+    }
+
+    [Fact]
+    public void Create_SetsAllPropertiesCorrectly()
+    {
+        var log = AgentExecutionLog.Create(TestExecutionId, 2, "completion", "response text", 250);
+
+        Assert.Equal(TestExecutionId, log.AgentExecutionId);
+        Assert.Equal(2, log.StepNumber);
+        Assert.Equal("completion", log.StepType);
+        Assert.Equal("response text", log.Content);
+        Assert.Equal(250, log.TokensUsed);
+    }
+
+    [Fact]
+    public void Create_SetsTimestampToCurrentTime()
+    {
+        var before = DateTimeOffset.UtcNow;
+        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", "content", 50);
+        var after = DateTimeOffset.UtcNow;
+
+        Assert.InRange(log.Timestamp, before, after);
+    }
+
+    [Fact]
+    public void Create_WithContentLongerThan2000Chars_TruncatesTo2000()
+    {
+        var longContent = new string('x', 3000);
+        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", longContent, 100);
+
+        Assert.Equal(2000, log.Content!.Length);
+    }
+
+    [Fact]
+    public void Create_WithContentAtExactly2000Chars_StoresAsIs()
+    {
+        var content = new string('x', 2000);
+        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", content, 100);
+
+        Assert.Equal(2000, log.Content!.Length);
+    }
+
+    [Fact]
+    public void Create_WithContentShorterThan2000Chars_StoresAsIs()
+    {
+        var content = "short content";
+        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", content, 100);
+
+        Assert.Equal("short content", log.Content);
+    }
+
+    [Fact]
+    public void Create_WithNullContent_StoresNull()
+    {
+        var log = AgentExecutionLog.Create(TestExecutionId, 1, "prompt", null, 100);
+        Assert.Null(log.Content);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/AgentExecutionTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/AgentExecutionTests.cs
new file mode 100644
index 0000000..c10a7e5
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/AgentExecutionTests.cs
@@ -0,0 +1,293 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class AgentExecutionTests
+{
+    private static AgentExecution CreatePending() =>
+        AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard);
+
+    private static AgentExecution CreateRunning()
+    {
+        var execution = CreatePending();
+        execution.MarkRunning();
+        return execution;
+    }
+
+    [Fact]
+    public void Create_SetsIdAsNonEmptyGuid()
+    {
+        var execution = CreatePending();
+        Assert.NotEqual(Guid.Empty, execution.Id);
+    }
+
+    [Fact]
+    public void Create_SetsStatusToPending()
+    {
+        var execution = CreatePending();
+        Assert.Equal(AgentExecutionStatus.Pending, execution.Status);
+    }
+
+    [Fact]
+    public void Create_SetsStartedAtToCurrentTime()
+    {
+        var before = DateTimeOffset.UtcNow;
+        var execution = CreatePending();
+        var after = DateTimeOffset.UtcNow;
+
+        Assert.InRange(execution.StartedAt, before, after);
+    }
+
+    [Fact]
+    public void Create_SetsAgentTypeFromParameter()
+    {
+        var execution = AgentExecution.Create(AgentCapabilityType.Social, ModelTier.Fast);
+        Assert.Equal(AgentCapabilityType.Social, execution.AgentType);
+    }
+
+    [Fact]
+    public void Create_SetsModelUsedFromParameter()
+    {
+        var execution = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Advanced);
+        Assert.Equal(ModelTier.Advanced, execution.ModelUsed);
+    }
+
+    [Fact]
+    public void Create_WithNullContentId_IsValid()
+    {
+        var execution = AgentExecution.Create(AgentCapabilityType.Analytics, ModelTier.Fast);
+        Assert.Null(execution.ContentId);
+    }
+
+    [Fact]
+    public void Create_WithContentId_StoresCorrectly()
+    {
+        var contentId = Guid.NewGuid();
+        var execution = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard, contentId);
+        Assert.Equal(contentId, execution.ContentId);
+    }
+
+    [Fact]
+    public void MarkRunning_SetsStatusToRunning()
+    {
+        var execution = CreatePending();
+        execution.MarkRunning();
+        Assert.Equal(AgentExecutionStatus.Running, execution.Status);
+    }
+
+    [Fact]
+    public void MarkRunning_ThrowsWhenStatusIsNotPending()
+    {
+        var execution = CreateRunning();
+        Assert.Throws<InvalidOperationException>(() => execution.MarkRunning());
+    }
+
+    [Fact]
+    public void Complete_SetsStatusToCompleted()
+    {
+        var execution = CreateRunning();
+        execution.Complete();
+        Assert.Equal(AgentExecutionStatus.Completed, execution.Status);
+    }
+
+    [Fact]
+    public void Complete_SetsCompletedAtAndDuration()
+    {
+        var execution = CreateRunning();
+        execution.Complete();
+
+        Assert.NotNull(execution.CompletedAt);
+        Assert.NotNull(execution.Duration);
+        Assert.True(execution.Duration.Value >= TimeSpan.Zero);
+    }
+
+    [Fact]
+    public void Complete_WithOutputSummary_StoresIt()
+    {
+        var execution = CreateRunning();
+        execution.Complete("Generated blog post about AI");
+        Assert.Equal("Generated blog post about AI", execution.OutputSummary);
+    }
+
+    [Fact]
+    public void Complete_ThrowsWhenStatusIsNotRunning()
+    {
+        var execution = CreatePending();
+        Assert.Throws<InvalidOperationException>(() => execution.Complete());
+    }
+
+    [Fact]
+    public void Fail_SetsStatusToFailed()
+    {
+        var execution = CreateRunning();
+        execution.Fail("API rate limit exceeded");
+        Assert.Equal(AgentExecutionStatus.Failed, execution.Status);
+    }
+
+    [Fact]
+    public void Fail_SetsErrorAndCompletedAt()
+    {
+        var execution = CreateRunning();
+        execution.Fail("Timeout");
+
+        Assert.Equal("Timeout", execution.Error);
+        Assert.NotNull(execution.CompletedAt);
+        Assert.NotNull(execution.Duration);
+    }
+
+    [Fact]
+    public void Fail_FromPending_Succeeds()
+    {
+        var execution = CreatePending();
+        execution.Fail("Budget exceeded before start");
+        Assert.Equal(AgentExecutionStatus.Failed, execution.Status);
+    }
+
+    [Fact]
+    public void Fail_ThrowsWhenAlreadyCompleted()
+    {
+        var execution = CreateRunning();
+        execution.Complete();
+        Assert.Throws<InvalidOperationException>(() => execution.Fail("Too late"));
+    }
+
+    [Fact]
+    public void Cancel_SetsStatusToCancelled()
+    {
+        var execution = CreateRunning();
+        execution.Cancel();
+        Assert.Equal(AgentExecutionStatus.Cancelled, execution.Status);
+    }
+
+    [Fact]
+    public void Cancel_SetsCompletedAtAndDuration()
+    {
+        var execution = CreateRunning();
+        execution.Cancel();
+
+        Assert.NotNull(execution.CompletedAt);
+        Assert.NotNull(execution.Duration);
+    }
+
+    [Fact]
+    public void Cancel_FromPending_Succeeds()
+    {
+        var execution = CreatePending();
+        execution.Cancel();
+        Assert.Equal(AgentExecutionStatus.Cancelled, execution.Status);
+    }
+
+    [Fact]
+    public void Cancel_ThrowsWhenAlreadyCompleted()
+    {
+        var execution = CreateRunning();
+        execution.Complete();
+        Assert.Throws<InvalidOperationException>(() => execution.Cancel());
+    }
+
+    [Fact]
+    public void Cancel_ThrowsWhenAlreadyFailed()
+    {
+        var execution = CreateRunning();
+        execution.Fail("Error");
+        Assert.Throws<InvalidOperationException>(() => execution.Cancel());
+    }
+
+    [Fact]
+    public void RecordUsage_SetsAllTokenAndCostFields()
+    {
+        var execution = CreateRunning();
+        execution.RecordUsage("claude-sonnet-4-5-20250929", 1000, 500, 200, 100, 0.0105m);
+
+        Assert.Equal("claude-sonnet-4-5-20250929", execution.ModelId);
+        Assert.Equal(1000, execution.InputTokens);
+        Assert.Equal(500, execution.OutputTokens);
+        Assert.Equal(200, execution.CacheReadTokens);
+        Assert.Equal(100, execution.CacheCreationTokens);
+        Assert.Equal(0.0105m, execution.Cost);
+    }
+
+    [Fact]
+    public void RecordUsage_CanBeCalledOnRunningExecution()
+    {
+        var execution = CreateRunning();
+        var exception = Record.Exception(() =>
+            execution.RecordUsage("claude-haiku-4-5", 100, 50, 0, 0, 0.001m));
+        Assert.Null(exception);
+    }
+
+    [Theory]
+    [InlineData(AgentExecutionStatus.Pending, AgentExecutionStatus.Running, true)]
+    [InlineData(AgentExecutionStatus.Pending, AgentExecutionStatus.Failed, true)]
+    [InlineData(AgentExecutionStatus.Pending, AgentExecutionStatus.Cancelled, true)]
+    [InlineData(AgentExecutionStatus.Running, AgentExecutionStatus.Completed, true)]
+    [InlineData(AgentExecutionStatus.Running, AgentExecutionStatus.Failed, true)]
+    [InlineData(AgentExecutionStatus.Running, AgentExecutionStatus.Cancelled, true)]
+    [InlineData(AgentExecutionStatus.Completed, AgentExecutionStatus.Failed, false)]
+    [InlineData(AgentExecutionStatus.Completed, AgentExecutionStatus.Cancelled, false)]
+    [InlineData(AgentExecutionStatus.Failed, AgentExecutionStatus.Running, false)]
+    [InlineData(AgentExecutionStatus.Cancelled, AgentExecutionStatus.Running, false)]
+    public void StatusTransitions_OnlyValidOnesAllowed(
+        AgentExecutionStatus from, AgentExecutionStatus to, bool shouldSucceed)
+    {
+        var execution = CreatePending();
+        TransitionToState(execution, from);
+
+        if (shouldSucceed)
+        {
+            TransitionTo(execution, to);
+            Assert.Equal(to, execution.Status);
+        }
+        else
+        {
+            Assert.ThrowsAny<InvalidOperationException>(() => TransitionTo(execution, to));
+        }
+    }
+
+    private static void TransitionToState(AgentExecution execution, AgentExecutionStatus target)
+    {
+        if (execution.Status == target) return;
+
+        switch (target)
+        {
+            case AgentExecutionStatus.Pending:
+                break;
+            case AgentExecutionStatus.Running:
+                execution.MarkRunning();
+                break;
+            case AgentExecutionStatus.Completed:
+                execution.MarkRunning();
+                execution.Complete();
+                break;
+            case AgentExecutionStatus.Failed:
+                execution.MarkRunning();
+                execution.Fail("Test failure");
+                break;
+            case AgentExecutionStatus.Cancelled:
+                execution.Cancel();
+                break;
+        }
+    }
+
+    private static void TransitionTo(AgentExecution execution, AgentExecutionStatus target)
+    {
+        switch (target)
+        {
+            case AgentExecutionStatus.Running:
+                execution.MarkRunning();
+                break;
+            case AgentExecutionStatus.Completed:
+                execution.Complete();
+                break;
+            case AgentExecutionStatus.Failed:
+                execution.Fail("Test error");
+                break;
+            case AgentExecutionStatus.Cancelled:
+                execution.Cancel();
+                break;
+            default:
+                throw new ArgumentOutOfRangeException(nameof(target));
+        }
+    }
+}
