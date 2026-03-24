using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public sealed class ContentPipeline : IContentPipeline
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ISidecarClient _sidecarClient;
    private readonly IBrandVoiceService _brandVoiceService;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IPipelineEventBroadcaster _broadcaster;
    private readonly ILogger<ContentPipeline> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ContentPipeline(
        IApplicationDbContext dbContext,
        ISidecarClient sidecarClient,
        IBrandVoiceService brandVoiceService,
        IWorkflowEngine workflowEngine,
        IPipelineEventBroadcaster broadcaster,
        ILogger<ContentPipeline> logger)
    {
        _dbContext = dbContext;
        _sidecarClient = sidecarClient;
        _brandVoiceService = brandVoiceService;
        _workflowEngine = workflowEngine;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task<Result<Guid>> CreateFromTopicAsync(ContentCreationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            return Result<Guid>.Failure(ErrorCode.ValidationFailed, "Topic is required");
        }

        var content = Content.Create(
            request.Type,
            body: string.Empty,
            title: null,
            request.TargetPlatforms);

        content.ParentContentId = request.ParentContentId;
        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(
            new { topic = request.Topic, outline = request.Outline }, JsonOptions);

        if (request.Parameters is not null)
        {
            foreach (var (key, value) in request.Parameters)
            {
                content.Metadata.PlatformSpecificData[key] = value;
            }
        }

        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync(ct);

        _ = _broadcaster.BroadcastAsync(new PipelineEvent(
            "pipeline:created",
            JsonSerializer.Serialize(new
            {
                contentId = content.Id,
                title = content.Title,
                platform = content.TargetPlatforms.Length > 0 ? content.TargetPlatforms[0].ToString() : "Unknown",
                contentType = content.ContentType.ToString(),
                timestamp = DateTimeOffset.UtcNow
            }, JsonOptions)));

        return Result<Guid>.Success(content.Id);
    }

    public async Task<Result<string>> GenerateOutlineAsync(Guid contentId, CancellationToken ct)
    {
        var content = await _dbContext.Contents.FindAsync([contentId], ct);
        if (content is null)
        {
            return Result<string>.NotFound($"Content {contentId} not found");
        }

        var (topic, _) = ParseGenerationContext(content.Metadata.AiGenerationContext);
        var prompt = $"Generate a detailed outline for a {content.ContentType} about: {topic}";

        var (text, _, _, _, error) = await ConsumeEventStreamAsync(prompt, ct);
        if (text is null)
        {
            return Result<string>.Failure(ErrorCode.InternalError, error ?? "Sidecar returned no text");
        }

        content.Metadata.AiGenerationContext = JsonSerializer.Serialize(
            new { topic, outline = text }, JsonOptions);
        await _dbContext.SaveChangesAsync(ct);

        return Result<string>.Success(text);
    }

    public async Task<Result<string>> GenerateDraftAsync(Guid contentId, CancellationToken ct)
    {
        var content = await _dbContext.Contents.FindAsync([contentId], ct);
        if (content is null)
        {
            return Result<string>.NotFound($"Content {contentId} not found");
        }

        var (topic, outline) = ParseGenerationContext(content.Metadata.AiGenerationContext);
        var brandContext = await LoadBrandContextAsync(ct);
        var seoKeywords = content.Metadata.SeoKeywords.Count > 0
            ? string.Join(", ", content.Metadata.SeoKeywords)
            : null;

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine($"Write a {content.ContentType} about: {topic}");
        if (outline is not null)
            promptBuilder.AppendLine($"\nOutline:\n{outline}");
        if (brandContext is not null)
            promptBuilder.AppendLine($"\nBrand voice: {brandContext}");
        if (seoKeywords is not null)
            promptBuilder.AppendLine($"\nSEO keywords: {seoKeywords}");

        var prompt = promptBuilder.ToString();
        var (text, filePath, inputTokens, outputTokens, draftError) = await ConsumeEventStreamAsync(prompt, ct);

        if (text is null)
        {
            return Result<string>.Failure(ErrorCode.InternalError, draftError ?? "Sidecar returned no text");
        }

        content.Body = text;

        if (filePath is not null)
        {
            content.Metadata.PlatformSpecificData["filePath"] = filePath;
        }

        content.Metadata.TokensUsed = inputTokens + outputTokens;
        await _dbContext.SaveChangesAsync(ct);

        return Result<string>.Success(text);
    }

    public async Task<Result<string>> GeneratePlatformDraftAsync(
        Guid contentId, PlatformType platform, string parentBody, CancellationToken ct)
    {
        var content = await _dbContext.Contents.FindAsync([contentId], ct);
        if (content is null)
        {
            return Result<string>.NotFound($"Content {contentId} not found");
        }

        var brandContext = await LoadBrandContextAsync(ct);
        var systemPrompt = GetPlatformSystemPrompt(platform);

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("Rewrite the following content for the target platform.");
        promptBuilder.AppendLine($"\nOriginal content:\n{parentBody}");
        if (brandContext is not null)
            promptBuilder.AppendLine($"\nBrand voice: {brandContext}");

        var prompt = promptBuilder.ToString();
        var (text, _, inputTokens, outputTokens, error) = await ConsumeEventStreamAsync(prompt, ct, systemPrompt);

        if (text is null)
        {
            return Result<string>.Failure(ErrorCode.InternalError, error ?? "Sidecar returned no text");
        }

        content.Body = text;
        content.Metadata.TokensUsed = inputTokens + outputTokens;
        await _dbContext.SaveChangesAsync(ct);

        return Result<string>.Success(text);
    }

    private static string GetPlatformSystemPrompt(PlatformType platform) => platform switch
    {
        PlatformType.LinkedIn => "You are a professional LinkedIn content writer for a tech thought leader. " +
            "Rewrite the provided content as a LinkedIn post. Use an authoritative, insightful tone. " +
            "Structure for readability with short paragraphs and line breaks. Include 3-5 relevant hashtags at the end. " +
            "Maximum 3000 characters. Write in a humanized, conversational style. Never use em-dashes.",
        PlatformType.TwitterX => "You are a sharp, opinionated tech Twitter writer. " +
            "Rewrite the provided content as a single tweet (max 280 characters) or a thread if needed. " +
            "Be punchy, direct, and credible to the dev community. No fluff, no corporate-speak. " +
            "Write in a humanized, natural voice. Never use em-dashes.",
        PlatformType.PersonalBlog => "You are a blog content writer. " +
            "Rewrite the provided content as a blog teaser/excerpt that drives readers to the full article. " +
            "Include a compelling hook, key takeaways preview, and a call-to-action. " +
            "Be SEO-conscious with natural keyword placement. Never use em-dashes.",
        _ => "Rewrite the provided content for social media. Keep it concise and engaging. Never use em-dashes.",
    };

    public async Task<Result<BrandVoiceScore>> ValidateVoiceAsync(Guid contentId, CancellationToken ct)
    {
        var content = await _dbContext.Contents.FindAsync([contentId], ct);
        if (content is null)
        {
            return Result<BrandVoiceScore>.NotFound($"Content {contentId} not found");
        }

        var scoreResult = await _brandVoiceService.ScoreContentAsync(contentId, ct);
        if (!scoreResult.IsSuccess)
        {
            return scoreResult;
        }

        content.Metadata.PlatformSpecificData["brandVoiceScore"] =
            JsonSerializer.Serialize(scoreResult.Value, JsonOptions);
        await _dbContext.SaveChangesAsync(ct);

        return scoreResult;
    }

    public async Task<Result<MediatR.Unit>> SubmitForReviewAsync(Guid contentId, CancellationToken ct)
    {
        var content = await _dbContext.Contents.FindAsync([contentId], ct);
        if (content is null)
        {
            return Result<MediatR.Unit>.NotFound($"Content {contentId} not found");
        }

        var transitionResult = await _workflowEngine.TransitionAsync(
            contentId, ContentStatus.Review,
            "Submitted via content pipeline", ActorType.System, ct);

        if (!transitionResult.IsSuccess)
        {
            return Result<MediatR.Unit>.Failure(transitionResult.ErrorCode, transitionResult.Errors.ToArray());
        }

        if (await _workflowEngine.ShouldAutoApproveAsync(contentId, ct))
        {
            var approvalResult = await _workflowEngine.TransitionAsync(
                contentId, ContentStatus.Approved,
                "Auto-approved by autonomy policy", ActorType.System, ct);

            if (!approvalResult.IsSuccess)
            {
                _logger.LogWarning(
                    "Auto-approval failed for content {ContentId}: {Errors}",
                    contentId, string.Join("; ", approvalResult.Errors));
            }
        }

        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
    }

    private async Task<(string? Text, string? FilePath, int InputTokens, int OutputTokens, string? Error)>
        ConsumeEventStreamAsync(string prompt, CancellationToken ct, string? systemPrompt = null)
    {
        string? lastSummary = null;
        string? filePath = null;
        int inputTokens = 0, outputTokens = 0;

        await foreach (var evt in _sidecarClient.SendTaskAsync(prompt, systemPrompt, null, ct))
        {
            switch (evt)
            {
                case ChatEvent { EventType: "summary", Text: not null } chat:
                    lastSummary = chat.Text;
                    break;
                case FileChangeEvent file:
                    filePath = file.FilePath;
                    break;
                case TaskCompleteEvent complete:
                    inputTokens = complete.InputTokens;
                    outputTokens = complete.OutputTokens;
                    break;
                case ErrorEvent error:
                    _logger.LogError("Sidecar error during content pipeline: {Message}", error.Message);
                    return (null, null, 0, 0, error.Message);
            }
        }

        return (lastSummary, filePath, inputTokens, outputTokens, null);
    }

    private static (string? Topic, string? Outline) ParseGenerationContext(string? json)
    {
        if (string.IsNullOrEmpty(json)) return (null, null);

        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            var topic = doc.TryGetProperty("topic", out var t) ? t.GetString() : null;
            var outline = doc.TryGetProperty("outline", out var o) && o.ValueKind != JsonValueKind.Null
                ? o.GetString() : null;
            return (topic, outline);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private async Task<string?> LoadBrandContextAsync(CancellationToken ct)
    {
        var profile = await _dbContext.BrandProfiles.FirstOrDefaultAsync(p => p.IsActive, ct);
        if (profile is null) return null;

        return $"{profile.PersonaDescription}. Tone: {string.Join(", ", profile.ToneDescriptors)}. " +
               $"Style: {profile.StyleGuidelines}";
    }
}
