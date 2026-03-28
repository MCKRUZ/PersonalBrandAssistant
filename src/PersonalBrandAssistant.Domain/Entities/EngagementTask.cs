using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class EngagementTask : AuditableEntityBase
{
    public PlatformType Platform { get; set; }
    public EngagementTaskType TaskType { get; set; }
    public string TargetCriteria { get; set; } = "";
    public string CronExpression { get; set; } = "";
    public bool IsEnabled { get; set; }
    public bool AutoRespond { get; set; }
    public DateTimeOffset? LastExecutedAt { get; set; }
    public DateTimeOffset? NextExecutionAt { get; set; }
    public int MaxActionsPerExecution { get; set; } = 3;
    public SchedulingMode SchedulingMode { get; set; } = SchedulingMode.HumanLike;
    public bool SkippedLastExecution { get; set; }
    public ICollection<EngagementExecution> Executions { get; } = new List<EngagementExecution>();
}
