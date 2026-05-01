using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public sealed partial class AutoPublishGateService : IAutoPublishGateService
{
    [GeneratedRegex(@"https?://([^/\s""'<>]+)")]
    private static partial Regex UrlDomainPattern();

    private static readonly Dictionary<PlatformType, int> PlatformCharLimits = new()
    {
        [PlatformType.TwitterX] = 280,
        [PlatformType.LinkedIn] = 3000,
        [PlatformType.Instagram] = 2200,
        [PlatformType.YouTube] = 5000,
        [PlatformType.Reddit] = 40000,
        [PlatformType.PersonalBlog] = 100000,
        [PlatformType.Substack] = 100000,
    };

    private readonly IApplicationDbContext _dbContext;
    private readonly IBrandVoiceService _brandVoiceService;
    private readonly ILogger<AutoPublishGateService> _logger;

    public AutoPublishGateService(
        IApplicationDbContext dbContext,
        IBrandVoiceService brandVoiceService,
        ILogger<AutoPublishGateService> logger)
    {
        _dbContext = dbContext;
        _brandVoiceService = brandVoiceService;
        _logger = logger;
    }

    public async Task<Result<AutoPublishGateResult>> EvaluateAsync(
        Guid contentId, AutonomyLevel level, CancellationToken ct = default)
    {
        if (level < AutonomyLevel.AutoPublish)
            return Result<AutoPublishGateResult>.Success(new AutoPublishGateResult(true, []));

        var content = await _dbContext.Contents.FindAsync([contentId], ct);
        if (content is null)
            return Result<AutoPublishGateResult>.NotFound($"Content {contentId} not found");

        var config = await _dbContext.AutonomyConfigurations.FirstOrDefaultAsync(ct);
        if (config is null)
            return Result<AutoPublishGateResult>.Failure(ErrorCode.ValidationFailed, "No autonomy configuration found");

        var scoreResult = await _brandVoiceService.ScoreContentAsync(contentId, ct);
        if (!scoreResult.IsSuccess)
            return Result<AutoPublishGateResult>.Failure(scoreResult.ErrorCode, scoreResult.Errors.ToArray());

        var score = scoreResult.Value!;
        var failures = new List<string>();

        if (score.OverallScore < config.AutoPublishThreshold)
            failures.Add($"Brand voice score {score.OverallScore} is below threshold {config.AutoPublishThreshold}");

        if (score.RuleViolations.Count > 0)
            failures.Add($"Content has {score.RuleViolations.Count} rule violation(s): {string.Join("; ", score.RuleViolations)}");

        foreach (var platform in content.TargetPlatforms)
        {
            if (PlatformCharLimits.TryGetValue(platform, out var limit) && content.Body.Length > limit)
                failures.Add($"Content exceeds {platform} character limit ({content.Body.Length}/{limit})");
        }

        if (content.ScheduledAt is not null)
        {
            var hour = content.ScheduledAt.Value.Hour;
            if (hour < 8 || hour >= 22)
                failures.Add($"Scheduled time {content.ScheduledAt.Value:HH:mm} is outside posting hours (08:00-22:00)");
        }

        var domains = ExtractDomains(content.Body);
        var allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "matthewkruczek.ai", "github.com", "linkedin.com", "twitter.com", "x.com"
        };
        foreach (var domain in domains)
        {
            if (!allowlist.Contains(domain))
                failures.Add($"Link to non-allowlisted domain: {domain}");
        }

        var approved = failures.Count == 0;
        var result = new AutoPublishGateResult(approved, failures);

        _logger.LogInformation(
            "AutoPublish gate evaluation for content {ContentId}: {Result} ({FailureCount} failures)",
            contentId, approved ? "APPROVED" : "REJECTED", failures.Count);

        return Result<AutoPublishGateResult>.Success(result);
    }

    private static IReadOnlyList<string> ExtractDomains(string text)
    {
        return UrlDomainPattern().Matches(text)
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Distinct()
            .ToList();
    }
}
