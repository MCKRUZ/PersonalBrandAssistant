using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NCrontab;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.SocialServices;

public sealed class SocialEngagementService : ISocialEngagementService
{
    private readonly IApplicationDbContext _db;
    private readonly IEnumerable<ISocialEngagementAdapter> _adapters;
    private readonly ISidecarClient _sidecar;
    private readonly IDateTimeProvider _dateTime;
    private readonly IHumanScheduler _humanScheduler;
    private readonly ILogger<SocialEngagementService> _logger;

    public SocialEngagementService(
        IApplicationDbContext db,
        IEnumerable<ISocialEngagementAdapter> adapters,
        ISidecarClient sidecar,
        IDateTimeProvider dateTime,
        IHumanScheduler humanScheduler,
        ILogger<SocialEngagementService> logger)
    {
        _db = db;
        _adapters = adapters;
        _sidecar = sidecar;
        _dateTime = dateTime;
        _humanScheduler = humanScheduler;
        _logger = logger;
    }

    public async Task<Result<SocialStatsDto>> GetStatsAsync(CancellationToken ct)
    {
        var activeTasks = await _db.EngagementTasks.CountAsync(t => t.IsEnabled, ct);
        var totalExecutions = await _db.EngagementExecutions.CountAsync(ct);

        var totals = await _db.EngagementExecutions
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Attempted = g.Sum(e => e.ActionsAttempted),
                Succeeded = g.Sum(e => e.ActionsSucceeded),
            })
            .FirstOrDefaultAsync(ct);

        var attempted = totals?.Attempted ?? 0;
        var succeeded = totals?.Succeeded ?? 0;
        var successRate = attempted > 0 ? Math.Round((double)succeeded / attempted * 100, 1) : 0;

        return Result.Success(new SocialStatsDto(activeTasks, totalExecutions, successRate, succeeded));
    }

    public async Task<Result<SafetyStatusDto>> GetSafetyStatusAsync(CancellationToken ct)
    {
        var autonomy = await _db.AutonomyConfigurations.FirstOrDefaultAsync(ct);
        var autonomyLevel = autonomy?.GlobalLevel.ToString() ?? "SemiAuto";

        var enabledCount = await _db.EngagementTasks.CountAsync(t => t.IsEnabled, ct);
        var autoRespondCount = await _db.EngagementTasks.CountAsync(t => t.IsEnabled && t.AutoRespond, ct);

        return Result.Success(new SafetyStatusDto(autonomyLevel, autoRespondCount, enabledCount));
    }

    public async Task<Result<IReadOnlyList<EngagementTask>>> GetTasksAsync(CancellationToken ct)
    {
        var tasks = await _db.EngagementTasks
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<EngagementTask>>(tasks.AsReadOnly());
    }

    public async Task<Result<EngagementTask>> CreateTaskAsync(
        CreateEngagementTaskDto dto, CancellationToken ct)
    {
        if (!Enum.TryParse<PlatformType>(dto.Platform, ignoreCase: true, out var platform))
            return Result.ValidationFailure<EngagementTask>([$"Invalid platform: {dto.Platform}"]);

        if (!Enum.TryParse<EngagementTaskType>(dto.TaskType, ignoreCase: true, out var taskType))
            return Result.ValidationFailure<EngagementTask>([$"Invalid task type: {dto.TaskType}"]);

        if (!IsValidCron(dto.CronExpression))
            return Result.ValidationFailure<EngagementTask>(["Invalid cron expression"]);

        var schedulingMode = SchedulingMode.HumanLike;
        if (dto.SchedulingMode is not null &&
            Enum.TryParse<SchedulingMode>(dto.SchedulingMode, ignoreCase: true, out var sm))
            schedulingMode = sm;

        var task = new EngagementTask
        {
            Platform = platform,
            TaskType = taskType,
            TargetCriteria = dto.TargetCriteria,
            CronExpression = dto.CronExpression,
            IsEnabled = dto.IsEnabled,
            AutoRespond = dto.AutoRespond,
            MaxActionsPerExecution = Math.Clamp(dto.MaxActionsPerExecution, 1, 10),
            SchedulingMode = schedulingMode,
            NextExecutionAt = dto.IsEnabled ? ComputeNextExecution(dto.CronExpression) : null,
        };

        _db.EngagementTasks.Add(task);
        await _db.SaveChangesAsync(ct);
        return Result.Success(task);
    }

    public async Task<Result<Unit>> UpdateTaskAsync(
        Guid id, UpdateEngagementTaskDto dto, CancellationToken ct)
    {
        var task = await _db.EngagementTasks.FindAsync([id], ct);
        if (task is null)
            return Result.NotFound<Unit>("Engagement task not found");

        if (dto.TargetCriteria is not null)
            task.TargetCriteria = dto.TargetCriteria;

        if (dto.CronExpression is not null)
        {
            if (!IsValidCron(dto.CronExpression))
                return Result.ValidationFailure<Unit>(["Invalid cron expression"]);
            task.CronExpression = dto.CronExpression;
        }

        if (dto.IsEnabled.HasValue)
            task.IsEnabled = dto.IsEnabled.Value;

        if (dto.AutoRespond.HasValue)
            task.AutoRespond = dto.AutoRespond.Value;

        if (dto.MaxActionsPerExecution.HasValue)
            task.MaxActionsPerExecution = Math.Clamp(dto.MaxActionsPerExecution.Value, 1, 10);

        if (dto.SchedulingMode is not null &&
            Enum.TryParse<SchedulingMode>(dto.SchedulingMode, ignoreCase: true, out var sm))
            task.SchedulingMode = sm;

        task.NextExecutionAt = task.IsEnabled ? ComputeNextExecution(task.CronExpression) : null;

        await _db.SaveChangesAsync(ct);
        return Result.Success(Unit.Value);
    }

    public async Task<Result<Unit>> DeleteTaskAsync(Guid id, CancellationToken ct)
    {
        var task = await _db.EngagementTasks.FindAsync([id], ct);
        if (task is null)
            return Result.NotFound<Unit>("Engagement task not found");

        _db.EngagementTasks.Remove(task);
        await _db.SaveChangesAsync(ct);
        return Result.Success(Unit.Value);
    }

    public async Task<Result<EngagementExecution>> ExecuteTaskAsync(
        Guid taskId, CancellationToken ct)
    {
        var task = await _db.EngagementTasks.FindAsync([taskId], ct);
        if (task is null)
            return Result.NotFound<EngagementExecution>("Engagement task not found");

        var adapter = _adapters.FirstOrDefault(a => a.Platform == task.Platform);
        if (adapter is null)
            return Result.Failure<EngagementExecution>(ErrorCode.InternalError,
                $"No engagement adapter for platform {task.Platform}");

        var execution = new EngagementExecution
        {
            EngagementTaskId = task.Id,
            ExecutedAt = _dateTime.UtcNow,
        };

        var postsResult = await adapter.FindRelevantPostsAsync(
            task.TargetCriteria, task.MaxActionsPerExecution, ct);

        if (!postsResult.IsSuccess)
        {
            execution.ErrorMessage = string.Join("; ", postsResult.Errors);
            _db.EngagementExecutions.Add(execution);
            await UpdateTaskTimestamps(task, ct);
            return Result.Success(execution);
        }

        var targets = postsResult.Value!;
        var isHumanLike = task.SchedulingMode == SchedulingMode.HumanLike;
        var maxSessionActions = isHumanLike
            ? _humanScheduler.GetActionsForSession(task)
            : task.MaxActionsPerExecution;
        var effectiveTargets = targets.Take(maxSessionActions).ToList();

        // Anti-community-flood: skip targets whose community already has 2+ actions in 24h
        if (isHumanLike)
        {
            var since = _dateTime.UtcNow.AddHours(-24);
            var recentCommunityCounts = await _db.EngagementActions
                .Where(a => a.PerformedAt >= since && a.Succeeded)
                .GroupBy(a => a.TargetUrl)
                .Select(g => new { Url = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var communityActionCounts = new Dictionary<string, int>();
            foreach (var entry in recentCommunityCounts)
            {
                var community = ExtractCommunity(entry.Url);
                communityActionCounts[community] = communityActionCounts.GetValueOrDefault(community) + entry.Count;
            }

            effectiveTargets = effectiveTargets
                .Where(t => communityActionCounts.GetValueOrDefault(t.Community) < 2)
                .ToList();
        }

        execution.ActionsAttempted = effectiveTargets.Count;

        for (var i = 0; i < effectiveTargets.Count; i++)
        {
            var target = effectiveTargets[i];

            // Inter-action delay for human-like scheduling
            if (isHumanLike && i > 0)
                await Task.Delay(_humanScheduler.GetInterActionDelay(task), ct);

            var action = new EngagementAction
            {
                EngagementExecutionId = execution.Id,
                ActionType = task.TaskType,
                TargetUrl = target.PostUrl,
                PerformedAt = _dateTime.UtcNow,
            };

            if (task.TaskType == EngagementTaskType.Comment)
            {
                var commentText = await GenerateCommentAsync(task.Platform, target, ct);
                action.GeneratedContent = commentText;

                var postResult = await adapter.PostCommentAsync(target.PostId, commentText, ct);
                action.Succeeded = postResult.IsSuccess;
                action.PlatformPostId = postResult.IsSuccess ? postResult.Value : null;
                action.ErrorMessage = postResult.IsSuccess ? null : string.Join("; ", postResult.Errors);
            }

            execution.Actions.Add(action);
        }

        execution.ActionsSucceeded = execution.Actions.Count(a => a.Succeeded);

        _db.EngagementExecutions.Add(execution);
        await UpdateTaskTimestamps(task, ct);

        _logger.LogInformation(
            "Executed engagement task {TaskId}: {Succeeded}/{Attempted} actions succeeded",
            task.Id, execution.ActionsSucceeded, execution.ActionsAttempted);

        return Result.Success(execution);
    }

    public async Task<Result<IReadOnlyList<EngagementExecution>>> GetExecutionHistoryAsync(
        Guid taskId, int limit, CancellationToken ct)
    {
        var executions = await _db.EngagementExecutions
            .Where(e => e.EngagementTaskId == taskId)
            .Include(e => e.Actions)
            .OrderByDescending(e => e.ExecutedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<EngagementExecution>>(executions.AsReadOnly());
    }

    // --- Opportunities ---

    public async Task<Result<IReadOnlyList<DiscoveredOpportunity>>> DiscoverOpportunitiesAsync(
        CancellationToken ct)
    {
        var enabledTasks = await _db.EngagementTasks
            .Where(t => t.IsEnabled)
            .ToListAsync(ct);

        if (enabledTasks.Count == 0)
            return Result.Success<IReadOnlyList<DiscoveredOpportunity>>(Array.Empty<DiscoveredOpportunity>());

        var keywords = await _db.InterestKeywords.ToListAsync(ct);

        var excludedUrls = await _db.OpportunityActions
            .Where(oa => oa.Status == OpportunityStatus.Dismissed || oa.Status == OpportunityStatus.Engaged)
            .Select(oa => oa.PostUrl)
            .ToListAsync(ct);
        var excludedSet = excludedUrls.ToHashSet();

        var opportunities = new List<DiscoveredOpportunity>();
        var seenUrls = new HashSet<string>();

        foreach (var task in enabledTasks)
        {
            var adapter = _adapters.FirstOrDefault(a => a.Platform == task.Platform);
            if (adapter is null) continue;

            var postsResult = await adapter.FindRelevantPostsAsync(task.TargetCriteria, 10, ct);
            if (!postsResult.IsSuccess) continue;

            foreach (var target in postsResult.Value!)
            {
                if (excludedSet.Contains(target.PostUrl) || !seenUrls.Add(target.PostUrl))
                    continue;

                var (impactScore, category) = ScoreOpportunity(target.Title, target.Content, keywords);

                opportunities.Add(new DiscoveredOpportunity(
                    target.PostId,
                    target.PostUrl,
                    target.Title.Length > 100 ? target.Title[..100] : target.Title,
                    target.Content.Length > 200 ? target.Content[..200] : target.Content,
                    target.Community,
                    task.Platform.ToString(),
                    _dateTime.UtcNow,
                    impactScore,
                    category));
            }
        }

        return Result.Success<IReadOnlyList<DiscoveredOpportunity>>(opportunities.AsReadOnly());
    }

    public async Task<Result<EngagementAction>> EngageSingleAsync(
        EngageSingleDto dto, CancellationToken ct)
    {
        if (!Enum.TryParse<PlatformType>(dto.Platform, ignoreCase: true, out var platform))
            return Result.ValidationFailure<EngagementAction>([$"Invalid platform: {dto.Platform}"]);

        var adapter = _adapters.FirstOrDefault(a => a.Platform == platform);
        if (adapter is null)
            return Result.Failure<EngagementAction>(ErrorCode.InternalError,
                $"No engagement adapter for platform {platform}");

        var target = new EngagementTarget(dto.PostId, dto.PostUrl, dto.Title, dto.Content, dto.Community);
        var commentText = await GenerateCommentAsync(platform, target, ct);

        var postResult = await adapter.PostCommentAsync(target.PostId, commentText, ct);

        var action = new EngagementAction
        {
            ActionType = EngagementTaskType.Comment,
            TargetUrl = dto.PostUrl,
            GeneratedContent = commentText,
            Succeeded = postResult.IsSuccess,
            PlatformPostId = postResult.IsSuccess ? postResult.Value : null,
            ErrorMessage = postResult.IsSuccess ? null : string.Join("; ", postResult.Errors),
            PerformedAt = _dateTime.UtcNow,
        };
        _db.EngagementActions.Add(action);

        // Record as engaged
        await UpsertOpportunityAction(dto.PostUrl, platform, OpportunityStatus.Engaged, ct);

        return Result.Success(action);
    }

    public async Task<Result<Unit>> DismissOpportunityAsync(
        string postUrl, PlatformType platform, CancellationToken ct)
    {
        await UpsertOpportunityAction(postUrl, platform, OpportunityStatus.Dismissed, ct);
        return Result.Success(Unit.Value);
    }

    public async Task<Result<Unit>> SaveOpportunityAsync(
        string postUrl, PlatformType platform, CancellationToken ct)
    {
        await UpsertOpportunityAction(postUrl, platform, OpportunityStatus.SavedForLater, ct);
        return Result.Success(Unit.Value);
    }

    public async Task<Result<IReadOnlyList<DiscoveredOpportunity>>> GetSavedOpportunitiesAsync(
        CancellationToken ct)
    {
        var saved = await _db.OpportunityActions
            .Where(oa => oa.Status == OpportunityStatus.SavedForLater)
            .OrderByDescending(oa => oa.CreatedAt)
            .ToListAsync(ct);

        var opportunities = saved.Select(oa => new DiscoveredOpportunity(
            "", oa.PostUrl, "", "", "", oa.Platform.ToString(), oa.CreatedAt)).ToList();

        return Result.Success<IReadOnlyList<DiscoveredOpportunity>>(opportunities.AsReadOnly());
    }

    private async Task UpsertOpportunityAction(
        string postUrl, PlatformType platform, OpportunityStatus status, CancellationToken ct)
    {
        var existing = await _db.OpportunityActions
            .FirstOrDefaultAsync(oa => oa.Platform == platform && oa.PostUrl == postUrl, ct);

        if (existing is not null)
        {
            existing.Status = status;
        }
        else
        {
            _db.OpportunityActions.Add(new OpportunityAction
            {
                PostUrl = postUrl,
                Platform = platform,
                Status = status,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<string> GenerateCommentAsync(
        PlatformType platform, EngagementTarget target, CancellationToken ct)
    {
        var lengthDirective = GetRandomLengthDirective();
        var prompt = $"""
            You are engaging on {platform}.
            Post context: {target.Title} — {target.Content}
            Community: {target.Community}

            Write a thoughtful, authentic comment that:
            - Adds genuine value (insight, experience, or helpful link)
            - Matches {platform} conventions (Reddit markdown, professional tone)
            - Doesn't sound promotional — no links to own content unless directly relevant
            - Is {lengthDirective}

            Reply with ONLY the comment text, no explanation.
            """;

        if (!_sidecar.IsConnected)
        {
            return $"Great insights on {target.Title}! This is an interesting perspective.";
        }

        var response = new System.Text.StringBuilder();
        await foreach (var evt in _sidecar.SendTaskAsync(prompt, null, null, ct))
        {
            if (evt is ChatEvent { Text: not null } chat)
                response.Append(chat.Text);
        }

        return response.Length > 0
            ? response.ToString().Trim()
            : $"Great insights on {target.Title}! This is an interesting perspective.";
    }

    private async Task UpdateTaskTimestamps(EngagementTask task, CancellationToken ct)
    {
        task.LastExecutedAt = _dateTime.UtcNow;

        if (task.IsEnabled)
        {
            var baseCronNext = ComputeNextExecution(task.CronExpression);
            task.NextExecutionAt = task.SchedulingMode == SchedulingMode.HumanLike && baseCronNext.HasValue
                ? _humanScheduler.ComputeNextHumanExecution(task, baseCronNext.Value)
                : baseCronNext;
        }
        else
        {
            task.NextExecutionAt = null;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static DateTimeOffset? ComputeNextExecution(string cronExpression)
    {
        var schedule = CrontabSchedule.Parse(cronExpression);
        var next = schedule.GetNextOccurrence(DateTime.UtcNow);
        return new DateTimeOffset(next, TimeSpan.Zero);
    }

    private static bool IsValidCron(string expression)
    {
        try
        {
            CrontabSchedule.Parse(expression);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetRandomLengthDirective()
    {
        var roll = Random.Shared.NextDouble();
        return roll switch
        {
            < 0.30 => "1-2 sentences",
            < 0.80 => "2-4 sentences",
            _ => "3-5 sentences",
        };
    }

    private static (string ImpactScore, string Category) ScoreOpportunity(
        string title, string contentPreview, IReadOnlyList<InterestKeyword> keywords)
    {
        if (keywords.Count == 0)
            return ("Medium", "General");

        var combinedText = $"{title} {contentPreview}";
        InterestKeyword? bestMatch = null;

        foreach (var keyword in keywords)
        {
            if (combinedText.Contains(keyword.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                if (bestMatch is null || keyword.Weight > bestMatch.Weight)
                    bestMatch = keyword;
            }
        }

        if (bestMatch is null)
            return ("Low", "General");

        var impact = bestMatch.Weight >= 0.8 ? "High" : "Medium";
        return (impact, bestMatch.Keyword);
    }

    private static string ExtractCommunity(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // Reddit: /r/communityname/...
            if (segments.Length >= 2 && segments[0] == "r")
                return segments[1];
            return uri.Host;
        }
        catch
        {
            return url;
        }
    }
}
