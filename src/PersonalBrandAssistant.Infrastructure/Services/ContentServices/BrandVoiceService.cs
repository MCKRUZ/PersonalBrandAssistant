using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public sealed partial class BrandVoiceService : IBrandVoiceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    private readonly IApplicationDbContext _dbContext;
    private readonly ISidecarClient _sidecar;
    private readonly IServiceProvider _serviceProvider;
    private readonly ContentEngineOptions _options;
    private readonly ILogger<BrandVoiceService> _logger;

    public BrandVoiceService(
        IApplicationDbContext dbContext,
        ISidecarClient sidecar,
        IServiceProvider serviceProvider,
        IOptions<ContentEngineOptions> options,
        ILogger<BrandVoiceService> logger)
    {
        _dbContext = dbContext;
        _sidecar = sidecar;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    public Result<IReadOnlyList<string>> RunRuleChecks(string text, BrandProfile profile)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(profile);

        var violations = new List<string>();
        var plainText = StripHtml(text);

        foreach (var term in profile.VocabularyPreferences.AvoidTerms)
        {
            var pattern = new Regex($@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase);
            if (pattern.IsMatch(plainText))
            {
                violations.Add($"Avoided term detected: '{term}'");
            }
        }

        if (profile.VocabularyPreferences.PreferredTerms.Count > 0)
        {
            var hasPreferred = profile.VocabularyPreferences.PreferredTerms
                .Any(t => new Regex($@"\b{Regex.Escape(t)}\b", RegexOptions.IgnoreCase).IsMatch(plainText));

            if (!hasPreferred)
            {
                violations.Add(
                    $"No preferred brand terms found. Consider including: {string.Join(", ", profile.VocabularyPreferences.PreferredTerms)}");
            }
        }

        return Result<IReadOnlyList<string>>.Success(violations);
    }

    public async Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct)
    {
        var content = await _dbContext.Contents.FindAsync([contentId], ct);
        if (content is null)
            return Result<BrandVoiceScore>.NotFound($"Content {contentId} not found");

        var profile = await _dbContext.BrandProfiles
            .FirstOrDefaultAsync(p => p.IsActive, ct);
        if (profile is null)
            return Result<BrandVoiceScore>.Failure(ErrorCode.ValidationFailed, "No active brand profile found");

        var ruleViolations = RunRuleChecks(content.Body, profile).Value ?? [];

        var prompt = BuildScoringPrompt(content.Body, profile);
        var (responseText, error) = await ConsumeEventStreamAsync(prompt, ct);

        if (error is not null)
            return Result<BrandVoiceScore>.Failure(ErrorCode.ValidationFailed, error);

        var dto = ParseScoreJson(responseText ?? "");
        if (dto is null)
            return Result<BrandVoiceScore>.Failure(ErrorCode.ValidationFailed,
                "Failed to parse brand voice score from LLM response");

        if (dto.OverallScore is < 0 or > 100 ||
            dto.ToneAlignment is < 0 or > 100 ||
            dto.VocabularyConsistency is < 0 or > 100 ||
            dto.PersonaFidelity is < 0 or > 100)
        {
            return Result<BrandVoiceScore>.Failure(ErrorCode.ValidationFailed,
                "LLM returned score values outside valid 0-100 range");
        }

        var score = new BrandVoiceScore(
            dto.OverallScore,
            dto.ToneAlignment,
            dto.VocabularyConsistency,
            dto.PersonaFidelity,
            dto.Issues ?? [],
            ruleViolations);

        content.Metadata.PlatformSpecificData["BrandVoiceScore"] =
            JsonSerializer.Serialize(score, JsonOptions);
        await _dbContext.SaveChangesAsync(ct);

        return Result<BrandVoiceScore>.Success(score);
    }

    public async Task<Result<MediatR.Unit>> ValidateAndGateAsync(
        Guid contentId, AutonomyLevel autonomy, CancellationToken ct)
    {
        var scoreResult = await ScoreContentAsync(contentId, ct);
        if (!scoreResult.IsSuccess)
            return Result<MediatR.Unit>.Failure(scoreResult.ErrorCode, scoreResult.Errors.ToArray());

        if (autonomy != AutonomyLevel.Autonomous)
            return Result<MediatR.Unit>.Success(MediatR.Unit.Value);

        var threshold = _options.BrandVoiceScoreThreshold;
        var maxAttempts = _options.MaxAutoRegenerateAttempts;

        if (scoreResult.Value!.OverallScore >= threshold)
            return Result<MediatR.Unit>.Success(MediatR.Unit.Value);

        // Resolved via IServiceProvider to break circular dependency:
        // BrandVoiceService -> IContentPipeline -> IBrandVoiceService
        var pipeline = _serviceProvider.GetRequiredService<IContentPipeline>();

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            _logger.LogInformation(
                "Brand voice score {Score} below threshold {Threshold} for content {ContentId}, regenerating (attempt {Attempt}/{Max})",
                scoreResult.Value!.OverallScore, threshold, contentId, attempt + 1, maxAttempts);

            var draftResult = await pipeline.GenerateDraftAsync(contentId, ct);
            if (!draftResult.IsSuccess)
                return Result<MediatR.Unit>.Failure(draftResult.ErrorCode, draftResult.Errors.ToArray());

            scoreResult = await ScoreContentAsync(contentId, ct);
            if (!scoreResult.IsSuccess)
                return Result<MediatR.Unit>.Failure(scoreResult.ErrorCode, scoreResult.Errors.ToArray());

            if (scoreResult.Value!.OverallScore >= threshold)
                return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
        }

        return Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed,
            $"Brand voice score {scoreResult.Value!.OverallScore} still below threshold {threshold} after {maxAttempts} regeneration attempts");
    }

    private static string BuildScoringPrompt(string contentBody, BrandProfile profile)
    {
        var plainText = StripHtml(contentBody);
        var tone = string.Join(", ", profile.ToneDescriptors);
        var preferred = string.Join(", ", profile.VocabularyPreferences.PreferredTerms);
        var avoided = string.Join(", ", profile.VocabularyPreferences.AvoidTerms);

        return $$"""
            You are a brand voice evaluator. Score the following content against the provided brand profile.
            Return ONLY valid JSON, no markdown fencing, no explanation.

            Brand Profile:
            - Tone: {{tone}}
            - Persona: {{profile.PersonaDescription}}
            - Style Guidelines: {{profile.StyleGuidelines}}
            - Preferred Terms: {{preferred}}
            - Avoided Terms: {{avoided}}

            Content to evaluate:
            {{plainText}}

            Expected JSON schema:
            {"overallScore": 0, "toneAlignment": 0, "vocabularyConsistency": 0, "personaFidelity": 0, "issues": []}

            Each dimension is 0-100. "issues" is an array of strings describing specific concerns.
            """;
    }

    private async Task<(string? Text, string? Error)> ConsumeEventStreamAsync(
        string prompt, CancellationToken ct)
    {
        try
        {
            var textParts = new List<string>();
            await foreach (var evt in _sidecar.SendTaskAsync(prompt, null, null, ct))
            {
                switch (evt)
                {
                    case ChatEvent { Text: not null } chat:
                        textParts.Add(chat.Text);
                        break;
                    case ErrorEvent error:
                        return (null, error.Message);
                }
            }

            return (string.Join("", textParts), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consuming sidecar event stream");
            return (null, ex.Message);
        }
    }

    private static BrandVoiceScoreDto? ParseScoreJson(string text)
    {
        // Strip markdown code fences if present
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline >= 0)
                cleaned = cleaned[(firstNewline + 1)..];
            if (cleaned.EndsWith("```"))
                cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();
        }

        try
        {
            return JsonSerializer.Deserialize<BrandVoiceScoreDto>(cleaned, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripHtml(string html)
    {
        var stripped = HtmlTagPattern().Replace(html, " ");
        stripped = WebUtility.HtmlDecode(stripped);
        stripped = WhitespacePattern().Replace(stripped, " ");
        return stripped.Trim();
    }

    private sealed class BrandVoiceScoreDto
    {
        [JsonPropertyName("overallScore")]
        public int OverallScore { get; set; }

        [JsonPropertyName("toneAlignment")]
        public int ToneAlignment { get; set; }

        [JsonPropertyName("vocabularyConsistency")]
        public int VocabularyConsistency { get; set; }

        [JsonPropertyName("personaFidelity")]
        public int PersonaFidelity { get; set; }

        [JsonPropertyName("issues")]
        public List<string>? Issues { get; set; }
    }
}
