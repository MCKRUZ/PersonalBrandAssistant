using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class AutomationRun : AuditableEntityBase
{
    private AutomationRun() { }

    public DateTimeOffset TriggeredAt { get; private init; }
    public AutomationRunStatus Status { get; private set; }
    public Guid? SelectedSuggestionId { get; set; }
    public Guid? PrimaryContentId { get; set; }
    public string? ImageFileId { get; set; }
    public string? ImagePrompt { get; set; }
    public string? SelectionReasoning { get; set; }
    public string? ErrorDetails { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public long DurationMs { get; private set; }
    public int PlatformVersionCount { get; set; }

    public static AutomationRun Create()
    {
        return new AutomationRun
        {
            TriggeredAt = DateTimeOffset.UtcNow,
            Status = AutomationRunStatus.Running,
        };
    }

    public void Complete(long durationMs)
    {
        Status = AutomationRunStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        DurationMs = durationMs;
    }

    public void Fail(string errorDetails, long durationMs)
    {
        Status = AutomationRunStatus.Failed;
        ErrorDetails = errorDetails;
        CompletedAt = DateTimeOffset.UtcNow;
        DurationMs = durationMs;
    }

    public void PartialFailure(string errorDetails, long durationMs)
    {
        Status = AutomationRunStatus.PartialFailure;
        ErrorDetails = errorDetails;
        CompletedAt = DateTimeOffset.UtcNow;
        DurationMs = durationMs;
    }
}
