using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public sealed class RepurposingService : IRepurposingService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ISidecarClient _sidecarClient;
    private readonly ContentEngineOptions _options;
    private readonly ILogger<RepurposingService> _logger;

    private static readonly Dictionary<PlatformType, ContentType> DefaultPlatformMapping = new()
    {
        [PlatformType.TwitterX] = ContentType.Thread,
        [PlatformType.LinkedIn] = ContentType.SocialPost,
        [PlatformType.Instagram] = ContentType.SocialPost,
        [PlatformType.YouTube] = ContentType.VideoDescription,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public RepurposingService(
        IApplicationDbContext dbContext,
        ISidecarClient sidecarClient,
        IOptions<ContentEngineOptions> options,
        ILogger<RepurposingService> logger)
    {
        _dbContext = dbContext;
        _sidecarClient = sidecarClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<Guid>>> RepurposeAsync(
        Guid sourceContentId, PlatformType[] targetPlatforms, CancellationToken ct)
    {
        var source = await _dbContext.Contents.FindAsync([sourceContentId], ct);
        if (source is null)
        {
            return Result<IReadOnlyList<Guid>>.NotFound($"Content {sourceContentId} not found");
        }

        if (source.TreeDepth >= _options.MaxTreeDepth)
        {
            return Result<IReadOnlyList<Guid>>.Failure(
                ErrorCode.ValidationFailed, "Maximum repurposing depth exceeded");
        }

        var sourcePlatform = source.TargetPlatforms.Length > 0
            ? source.TargetPlatforms[0]
            : (PlatformType?)null;

        // Load existing children for idempotency check
        var existingChildren = await _dbContext.Contents
            .Where(c => c.ParentContentId == sourceContentId)
            .ToListAsync(ct);

        var createdIds = new List<Guid>();

        foreach (var targetPlatform in targetPlatforms)
        {
            var contentType = DefaultPlatformMapping.GetValueOrDefault(targetPlatform, ContentType.SocialPost);

            // Idempotency: skip if child already exists for this combination
            var alreadyExists = existingChildren.Any(c =>
                c.RepurposeSourcePlatform == sourcePlatform &&
                c.ContentType == contentType &&
                c.TargetPlatforms.Contains(targetPlatform));

            if (alreadyExists)
            {
                _logger.LogInformation(
                    "Skipping repurpose for {Platform}/{ContentType} — child already exists for content {ContentId}",
                    targetPlatform, contentType, sourceContentId);
                continue;
            }

            var prompt = BuildRepurposePrompt(source, targetPlatform, contentType);
            var (text, error) = await ConsumeTextAsync(prompt, ct);

            if (text is null)
            {
                _logger.LogWarning(
                    "Sidecar returned no text for repurpose {Platform}: {Error}",
                    targetPlatform, error);
                continue;
            }

            var child = Content.Create(
                contentType,
                body: text,
                title: null,
                targetPlatforms: [targetPlatform]);

            child.ParentContentId = sourceContentId;
            child.TreeDepth = source.TreeDepth + 1;
            child.RepurposeSourcePlatform = sourcePlatform;

            _dbContext.Contents.Add(child);
            createdIds.Add(child.Id);
        }

        if (createdIds.Count > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
        }

        return Result<IReadOnlyList<Guid>>.Success(createdIds);
    }

    public async Task<Result<IReadOnlyList<RepurposingSuggestion>>> SuggestRepurposingAsync(
        Guid contentId, CancellationToken ct)
    {
        var content = await _dbContext.Contents.FindAsync([contentId], ct);
        if (content is null)
        {
            return Result<IReadOnlyList<RepurposingSuggestion>>.NotFound($"Content {contentId} not found");
        }

        var prompt = $"""
            Analyze this {content.ContentType} content and suggest platforms for repurposing.
            Content: {content.Body}

            Return a JSON array of suggestions, each with: platform (TwitterX|LinkedIn|Instagram|YouTube), suggestedType (BlogPost|SocialPost|Thread|VideoDescription), rationale, confidenceScore (0.0-1.0).
            Return ONLY the JSON array, no other text.
            """;

        var (text, _) = await ConsumeTextAsync(prompt, ct);
        if (text is null)
        {
            return Result<IReadOnlyList<RepurposingSuggestion>>.Failure(
                ErrorCode.InternalError, "Sidecar returned no suggestions");
        }

        try
        {
            var raw = JsonSerializer.Deserialize<List<SuggestionDto>>(text, JsonOptions) ?? [];
            var suggestions = raw
                .Where(s => Enum.TryParse<PlatformType>(s.Platform, true, out _)
                         && Enum.TryParse<ContentType>(s.SuggestedType, true, out _))
                .Select(s => new RepurposingSuggestion(
                    Enum.Parse<PlatformType>(s.Platform, true),
                    Enum.Parse<ContentType>(s.SuggestedType, true),
                    s.Rationale,
                    s.ConfidenceScore))
                .OrderByDescending(s => s.ConfidenceScore)
                .ToList();

            return Result<IReadOnlyList<RepurposingSuggestion>>.Success(suggestions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse repurposing suggestions from sidecar");
            return Result<IReadOnlyList<RepurposingSuggestion>>.Failure(
                ErrorCode.InternalError, "Failed to parse AI suggestions");
        }
    }

    public async Task<Result<IReadOnlyList<Content>>> GetContentTreeAsync(
        Guid rootId, CancellationToken ct)
    {
        var root = await _dbContext.Contents.FindAsync([rootId], ct);
        if (root is null)
        {
            return Result<IReadOnlyList<Content>>.NotFound($"Content {rootId} not found");
        }

        // Iterative BFS — one query per tree level, not full table load
        var descendants = new List<Content>();
        var currentLevel = new List<Guid> { rootId };

        while (currentLevel.Count > 0)
        {
            var children = await _dbContext.Contents
                .Where(c => c.ParentContentId != null && currentLevel.Contains(c.ParentContentId.Value))
                .ToListAsync(ct);

            if (children.Count == 0) break;

            descendants.AddRange(children);
            currentLevel = children.Select(c => c.Id).ToList();
        }

        return Result<IReadOnlyList<Content>>.Success(descendants);
    }

    private static string BuildRepurposePrompt(Content source, PlatformType targetPlatform, ContentType contentType)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Repurpose the following {source.ContentType} content for {targetPlatform} as a {contentType}.");
        builder.AppendLine();
        builder.AppendLine($"Source content:");
        builder.AppendLine(source.Body);
        builder.AppendLine();

        builder.AppendLine(targetPlatform switch
        {
            PlatformType.TwitterX => "Format as a Twitter/X thread. Max 280 chars per tweet. Use numbered tweets.",
            PlatformType.LinkedIn => "Format as a LinkedIn post. Professional tone. Max 3000 chars.",
            PlatformType.Instagram => "Format as an Instagram caption. Include relevant hashtags. Max 2200 chars.",
            PlatformType.YouTube => "Format as a YouTube video description with timestamps and links.",
            _ => "Adapt the content appropriately for the target platform.",
        });

        return builder.ToString();
    }

    private async Task<(string? Text, string? Error)> ConsumeTextAsync(string prompt, CancellationToken ct)
    {
        var textBuilder = new StringBuilder();

        await foreach (var evt in _sidecarClient.SendTaskAsync(prompt, null, null, ct))
        {
            switch (evt)
            {
                case ChatEvent { Text: not null } chat:
                    textBuilder.Append(chat.Text);
                    break;
                case ErrorEvent error:
                    _logger.LogError("Sidecar error during repurposing: {Message}", error.Message);
                    return (null, error.Message);
            }
        }

        var text = textBuilder.Length > 0 ? textBuilder.ToString() : null;
        return (text, null);
    }

    private record SuggestionDto(
        string Platform,
        string SuggestedType,
        string Rationale,
        float ConfidenceScore);
}
