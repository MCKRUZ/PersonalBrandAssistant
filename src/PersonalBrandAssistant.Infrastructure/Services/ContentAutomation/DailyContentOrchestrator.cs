using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentAutomation;

public sealed class DailyContentOrchestrator : IDailyContentOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ITrendMonitor _trendMonitor;
    private readonly IContentPipeline _contentPipeline;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly ISidecarClient _sidecarClient;
    private readonly IImagePromptService _imagePromptService;
    private readonly IImageGenerationService _imageGenerationService;
    private readonly IImageResizer _imageResizer;
    private readonly INotificationService _notificationService;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<DailyContentOrchestrator> _logger;

    public DailyContentOrchestrator(
        ITrendMonitor trendMonitor,
        IContentPipeline contentPipeline,
        IWorkflowEngine workflowEngine,
        ISidecarClient sidecarClient,
        IImagePromptService imagePromptService,
        IImageGenerationService imageGenerationService,
        IImageResizer imageResizer,
        INotificationService notificationService,
        IApplicationDbContext dbContext,
        ILogger<DailyContentOrchestrator> logger)
    {
        _trendMonitor = trendMonitor;
        _contentPipeline = contentPipeline;
        _workflowEngine = workflowEngine;
        _sidecarClient = sidecarClient;
        _imagePromptService = imagePromptService;
        _imageGenerationService = imageGenerationService;
        _imageResizer = imageResizer;
        _notificationService = notificationService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AutomationRunResult> ExecuteAsync(
        ContentAutomationOptions options, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var run = AutomationRun.Create();
        _dbContext.AutomationRuns.Add(run);
        await _dbContext.SaveChangesAsync(ct);

        try
        {
            // Ensure sidecar is connected
            if (!_sidecarClient.IsConnected)
            {
                await _sidecarClient.ConnectAsync(ct);
            }

            // Step 1: AI-curated trend selection
            var (suggestionId, reasoning, contentType) = await SelectTrendAsync(options, ct);
            run.SelectedSuggestionId = suggestionId;
            run.SelectionReasoning = reasoning;

            // Step 2: Primary content creation
            var acceptResult = await _trendMonitor.AcceptSuggestionAsync(suggestionId, ct, contentType);
            if (!acceptResult.IsSuccess)
            {
                return FailRun(run, stopwatch, "Failed to accept suggestion: " + string.Join("; ", acceptResult.Errors));
            }

            var primaryContentId = acceptResult.Value;
            run.PrimaryContentId = primaryContentId;

            await _contentPipeline.GenerateOutlineAsync(primaryContentId, ct);
            await _contentPipeline.GenerateDraftAsync(primaryContentId, ct);

            var primaryContent = await _dbContext.Contents.FindAsync([primaryContentId], ct);
            var parentBody = primaryContent!.Body;

            // Step 3: Platform-specific content generation
            var isSemiAuto = string.Equals(options.AutonomyLevel, "SemiAuto", StringComparison.OrdinalIgnoreCase);
            var children = new List<(Guid ContentId, PlatformType Platform)>();
            var parsedPlatforms = new List<PlatformType>();

            foreach (var platformStr in options.TargetPlatforms)
            {
                if (!Enum.TryParse<PlatformType>(platformStr, ignoreCase: true, out var platform))
                {
                    _logger.LogWarning("Invalid platform: {Platform}, skipping", platformStr);
                    continue;
                }

                parsedPlatforms.Add(platform);
                var autonomy = isSemiAuto ? AutonomyLevel.Manual : AutonomyLevel.Autonomous;
                var child = Content.Create(
                    primaryContent.ContentType, "", primaryContent.Title,
                    [platform], autonomy);
                child.ParentContentId = primaryContentId;
                _dbContext.Contents.Add(child);
                await _dbContext.SaveChangesAsync(ct);

                await _contentPipeline.GeneratePlatformDraftAsync(child.Id, platform, parentBody, ct);
                children.Add((child.Id, platform));
            }

            run.PlatformVersionCount = children.Count;

            // Step 4: Image generation
            if (options.ImageGeneration.Enabled)
            {
                var imagePrompt = await _imagePromptService.GeneratePromptAsync(parentBody, ct);
                run.ImagePrompt = imagePrompt;

                var imageResult = await _imageGenerationService.GenerateAsync(
                    imagePrompt, options.ImageGeneration, ct);

                if (!imageResult.Success)
                {
                    // Image failure: transition all to Review, notify, check circuit breaker
                    var allContentIds = new[] { primaryContentId }.Concat(children.Select(c => c.ContentId));
                    foreach (var contentId in allContentIds)
                    {
                        await _workflowEngine.TransitionAsync(
                            contentId, ContentStatus.Review, "Image generation failed", ActorType.System, ct);
                    }

                    await _notificationService.SendAsync(
                        NotificationType.AutomationImageFailed,
                        "Image generation failed",
                        imageResult.Error ?? "ComfyUI error",
                        primaryContentId, ct);

                    await CheckCircuitBreakerAsync(options, ct);
                    return FailRun(run, stopwatch, $"ComfyUI: {imageResult.Error}");
                }

                // Image success: resize and associate
                run.ImageFileId = imageResult.FileId;
                primaryContent.ImageFileId = imageResult.FileId;
                primaryContent.ImageRequired = true;

                var platformImages = await _imageResizer.ResizeForPlatformsAsync(
                    imageResult.FileId!, parsedPlatforms.ToArray(), ct);

                foreach (var (childId, platform) in children)
                {
                    var childContent = await _dbContext.Contents.FindAsync([childId], ct);
                    if (childContent is not null && platformImages.TryGetValue(platform, out var cropFileId))
                    {
                        childContent.ImageFileId = cropFileId;
                        childContent.ImageRequired = true;
                    }
                }

                await _dbContext.SaveChangesAsync(ct);
            }

            // Step 5: Brand validation (informational only)
            var allIds = new[] { primaryContentId }.Concat(children.Select(c => c.ContentId));
            foreach (var contentId in allIds)
            {
                await _contentPipeline.ValidateVoiceAsync(contentId, ct);
            }

            // Step 6: Workflow transitions
            foreach (var contentId in allIds)
            {
                await _contentPipeline.SubmitForReviewAsync(contentId, ct);

                if (!isSemiAuto)
                {
                    // Autonomous: transition to Scheduled for immediate publish
                    await _workflowEngine.TransitionAsync(
                        contentId, ContentStatus.Scheduled,
                        "Auto-scheduled by automation pipeline", ActorType.System, ct);

                    var content = await _dbContext.Contents.FindAsync([contentId], ct);
                    if (content is not null)
                    {
                        content.ScheduledAt = DateTimeOffset.UtcNow;
                    }
                }
            }

            await _dbContext.SaveChangesAsync(ct);

            // Send notification
            if (isSemiAuto)
            {
                await _notificationService.SendAsync(
                    NotificationType.ContentReadyForReview,
                    "Content ready for review",
                    $"{primaryContent.Title} - {children.Count} platform versions",
                    primaryContentId, ct);
            }
            else
            {
                await _notificationService.SendAsync(
                    NotificationType.AutomationPipelineCompleted,
                    "Content published",
                    $"Published {primaryContent.Title} to {string.Join(", ", parsedPlatforms)}. Duration: {stopwatch.ElapsedMilliseconds}ms",
                    primaryContentId, ct);
            }

            // Step 7: Record run
            stopwatch.Stop();
            run.Complete(stopwatch.ElapsedMilliseconds);
            await _dbContext.SaveChangesAsync(ct);

            return new AutomationRunResult(
                true, run.Id, primaryContentId, run.ImageFileId,
                run.PlatformVersionCount, null, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation pipeline failed");
            stopwatch.Stop();
            run.Fail(ex.Message, stopwatch.ElapsedMilliseconds);
            await _dbContext.SaveChangesAsync(ct);

            return new AutomationRunResult(
                false, run.Id, run.PrimaryContentId, null,
                0, ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<(Guid SuggestionId, string Reasoning, ContentType ContentType)> SelectTrendAsync(
        ContentAutomationOptions options, CancellationToken ct)
    {
        var suggestionsResult = await _trendMonitor.GetSuggestionsAsync(options.TopTrendsToConsider, ct);
        if (!suggestionsResult.IsSuccess || suggestionsResult.Value!.Count == 0)
        {
            throw new InvalidOperationException("No trends available");
        }

        var suggestions = suggestionsResult.Value;

        // Get recent topics for diversity
        var recentTopics = await _dbContext.Contents
            .Where(c => c.Status == ContentStatus.Published
                && c.PublishedAt >= DateTimeOffset.UtcNow.AddDays(-7))
            .Select(c => c.Title)
            .ToListAsync(ct);

        var suggestionList = string.Join("\n", suggestions.Select(s =>
            $"- ID: {s.Id}, Topic: {s.Topic}, Score: {s.RelevanceScore:F2}"));

        var recentList = recentTopics.Count > 0
            ? "Recent published topics (avoid repetition):\n" + string.Join("\n", recentTopics.Select(t => $"- {t}"))
            : "No recent publications.";

        var jsonFormat = "{\"suggestionId\": \"<guid>\", \"reasoning\": \"...\", \"contentType\": \"SocialPost|Thread|BlogPost\"}";
        var task = $"""
            Select the most compelling trending topic for content creation.

            Available topics:
            {suggestionList}

            {recentList}

            Consider: engagement potential, topic diversity, brand alignment.
            Return ONLY a JSON object with this format: {jsonFormat}
            """;

        var systemPrompt = "You are a content strategist. Pick the best topic and respond with ONLY valid JSON.";

        var responseText = await ConsumeSidecarResponseAsync(task, systemPrompt, ct);

        try
        {
            var response = JsonSerializer.Deserialize<TrendCurationResponse>(responseText, JsonOptions)
                ?? throw new JsonException("Null response");

            var contentType = Enum.TryParse<ContentType>(response.ContentType, ignoreCase: true, out var ct2)
                ? ct2 : ContentType.SocialPost;

            return (response.SuggestionId, response.Reasoning, contentType);
        }
        catch (JsonException)
        {
            // Retry once with explicit instructions
            _logger.LogWarning("Failed to parse trend curation response, retrying");
            var retryTask = task + "\n\nIMPORTANT: Your previous response was not valid JSON. Respond with ONLY a JSON object, no other text.";
            var retryText = await ConsumeSidecarResponseAsync(retryTask, systemPrompt, ct);

            var response = JsonSerializer.Deserialize<TrendCurationResponse>(retryText, JsonOptions)
                ?? throw new InvalidOperationException("Failed to parse trend curation response after retry");

            var contentType = Enum.TryParse<ContentType>(response.ContentType, ignoreCase: true, out var ct3)
                ? ct3 : ContentType.SocialPost;

            return (response.SuggestionId, response.Reasoning, contentType);
        }
    }

    private async Task<string> ConsumeSidecarResponseAsync(
        string task, string? systemPrompt, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var evt in _sidecarClient.SendTaskAsync(task, systemPrompt, null, ct))
        {
            switch (evt)
            {
                case ChatEvent { Text: not null } chat:
                    sb.Append(chat.Text);
                    break;
                case ErrorEvent error:
                    throw new InvalidOperationException($"Sidecar error: {error.Message}");
            }
        }

        var result = sb.ToString().Trim();
        if (string.IsNullOrEmpty(result))
            throw new InvalidOperationException("Sidecar returned empty response");

        return result;
    }

    private async Task CheckCircuitBreakerAsync(ContentAutomationOptions options, CancellationToken ct)
    {
        var threshold = options.ImageGeneration.CircuitBreakerThreshold;
        var recentFailures = await _dbContext.AutomationRuns
            .Where(r => r.Status == AutomationRunStatus.Failed
                && r.ErrorDetails != null
                && r.ErrorDetails.Contains("ComfyUI"))
            .OrderByDescending(r => r.TriggeredAt)
            .Take(threshold)
            .ToListAsync(ct);

        if (recentFailures.Count >= threshold)
        {
            await _notificationService.SendAsync(
                NotificationType.AutomationConsecutiveFailure,
                "ComfyUI circuit breaker tripped",
                $"ComfyUI has been unreachable for {threshold} consecutive runs. Image generation should be disabled.",
                null, ct);
        }
    }

    private AutomationRunResult FailRun(AutomationRun run, Stopwatch stopwatch, string error)
    {
        stopwatch.Stop();
        run.Fail(error, stopwatch.ElapsedMilliseconds);
        // Note: SaveChangesAsync is called by the caller after this returns
        return new AutomationRunResult(
            false, run.Id, run.PrimaryContentId, null,
            0, error, stopwatch.ElapsedMilliseconds);
    }

    private record TrendCurationResponse(Guid SuggestionId, string Reasoning, string ContentType);
}
