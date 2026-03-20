using MediatR;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ISocialEngagementService
{
    Task<Result<IReadOnlyList<EngagementTask>>> GetTasksAsync(CancellationToken ct);
    Task<Result<EngagementTask>> CreateTaskAsync(CreateEngagementTaskDto dto, CancellationToken ct);
    Task<Result<Unit>> UpdateTaskAsync(Guid id, UpdateEngagementTaskDto dto, CancellationToken ct);
    Task<Result<Unit>> DeleteTaskAsync(Guid id, CancellationToken ct);
    Task<Result<EngagementExecution>> ExecuteTaskAsync(Guid taskId, CancellationToken ct);
    Task<Result<IReadOnlyList<EngagementExecution>>> GetExecutionHistoryAsync(Guid taskId, int limit, CancellationToken ct);

    // Stats & Safety
    Task<Result<SocialStatsDto>> GetStatsAsync(CancellationToken ct);
    Task<Result<SafetyStatusDto>> GetSafetyStatusAsync(CancellationToken ct);

    // Opportunities
    Task<Result<IReadOnlyList<DiscoveredOpportunity>>> DiscoverOpportunitiesAsync(CancellationToken ct);
    Task<Result<EngagementAction>> EngageSingleAsync(EngageSingleDto dto, CancellationToken ct);
    Task<Result<Unit>> DismissOpportunityAsync(string postUrl, PlatformType platform, CancellationToken ct);
    Task<Result<Unit>> SaveOpportunityAsync(string postUrl, PlatformType platform, CancellationToken ct);
    Task<Result<IReadOnlyList<DiscoveredOpportunity>>> GetSavedOpportunitiesAsync(CancellationToken ct);
}

public record EngageSingleDto(
    string Platform,
    string PostId,
    string PostUrl,
    string Title,
    string Content,
    string Community);

public record CreateEngagementTaskDto(
    string Platform,
    string TaskType,
    string TargetCriteria,
    string CronExpression,
    bool IsEnabled,
    bool AutoRespond = false,
    int MaxActionsPerExecution = 3,
    string? SchedulingMode = null);

public record UpdateEngagementTaskDto(
    string? TargetCriteria = null,
    string? CronExpression = null,
    bool? IsEnabled = null,
    bool? AutoRespond = null,
    int? MaxActionsPerExecution = null,
    string? SchedulingMode = null);

public record SocialStatsDto(int ActiveTasks, int TotalExecutions, double SuccessRate, int TotalActions);

public record SafetyStatusDto(string AutonomyLevel, int AutoRespondTaskCount, int EnabledTaskCount);
