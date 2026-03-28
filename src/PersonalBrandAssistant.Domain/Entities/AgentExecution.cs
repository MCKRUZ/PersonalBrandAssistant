using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class AgentExecution : AuditableEntityBase
{
    private AgentExecution() { }

    public Guid? ContentId { get; private init; }
    public AgentCapabilityType AgentType { get; private init; }
    public AgentExecutionStatus Status { get; private set; } = AgentExecutionStatus.Pending;
    public ModelTier ModelUsed { get; private init; }
    public string? ModelId { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public int CacheReadTokens { get; private set; }
    public int CacheCreationTokens { get; private set; }
    public decimal Cost { get; private set; }
    public DateTimeOffset StartedAt { get; private init; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public TimeSpan? Duration { get; private set; }
    public string? Error { get; private set; }
    public string? OutputSummary { get; private set; }

    public static AgentExecution Create(
        AgentCapabilityType agentType,
        ModelTier modelTier,
        Guid? contentId = null) =>
        new()
        {
            AgentType = agentType,
            ModelUsed = modelTier,
            ContentId = contentId,
            Status = AgentExecutionStatus.Pending,
            StartedAt = DateTimeOffset.UtcNow,
        };

    public void MarkRunning()
    {
        if (Status != AgentExecutionStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot mark as running from {Status}. Must be Pending.");

        Status = AgentExecutionStatus.Running;
    }

    public void Complete(string? outputSummary = null)
    {
        if (Status != AgentExecutionStatus.Running)
            throw new InvalidOperationException(
                $"Cannot complete from {Status}. Must be Running.");

        Status = AgentExecutionStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        Duration = CompletedAt - StartedAt;
        OutputSummary = outputSummary;
    }

    public void Fail(string error)
    {
        if (Status is not (AgentExecutionStatus.Running or AgentExecutionStatus.Pending))
            throw new InvalidOperationException(
                $"Cannot fail from {Status}. Must be Running or Pending.");

        Status = AgentExecutionStatus.Failed;
        Error = error?.Length > 4000 ? error[..4000] : error;
        CompletedAt = DateTimeOffset.UtcNow;
        Duration = CompletedAt - StartedAt;
    }

    public void Cancel()
    {
        if (Status is AgentExecutionStatus.Completed or AgentExecutionStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot cancel from {Status}. Already in terminal state.");

        Status = AgentExecutionStatus.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
        Duration = CompletedAt - StartedAt;
    }

    public void RecordUsage(
        string modelId,
        int inputTokens,
        int outputTokens,
        int cacheReadTokens,
        int cacheCreationTokens,
        decimal cost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheReadTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheCreationTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(cost);

        ModelId = modelId;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CacheReadTokens = cacheReadTokens;
        CacheCreationTokens = cacheCreationTokens;
        Cost = cost;
    }
}
