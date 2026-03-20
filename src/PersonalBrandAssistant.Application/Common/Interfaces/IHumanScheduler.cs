using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IHumanScheduler
{
    WeeklyBehaviorProfile GetCurrentProfile(EngagementTask task);
    DateTimeOffset ComputeNextHumanExecution(EngagementTask task, DateTimeOffset baseCronNext);
    bool ShouldSkipExecution(EngagementTask task);
    TimeSpan GetInterActionDelay(EngagementTask task);
    int GetActionsForSession(EngagementTask task);
}
