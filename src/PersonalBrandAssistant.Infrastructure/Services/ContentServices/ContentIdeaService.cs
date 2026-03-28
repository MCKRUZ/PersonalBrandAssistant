using System.Text.Json;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public sealed class ContentIdeaService : IContentIdeaService
{
    private readonly ISidecarClient _sidecar;
    private readonly ILogger<ContentIdeaService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public ContentIdeaService(ISidecarClient sidecar, ILogger<ContentIdeaService> logger)
    {
        _sidecar = sidecar;
        _logger = logger;
    }

    public async Task<Result<ContentIdeaRecommendation>> AnalyzeStoryAsync(
        string storyText, string? sourceUrl, CancellationToken ct)
    {
        var prompt = BuildPrompt(storyText, sourceUrl);

        if (!_sidecar.IsConnected)
            await _sidecar.ConnectAsync(ct);

        var (text, error) = await ConsumeTextAsync(prompt, ct);
        if (text is null)
        {
            _logger.LogWarning("Story analysis returned no content: {Error}", error);
            return Result<ContentIdeaRecommendation>.Failure(ErrorCode.InternalError,
                error ?? "No response from AI");
        }

        try
        {
            var json = ExtractJson(text);
            var dto = JsonSerializer.Deserialize<RecommendationDto>(json, JsonOptions);
            if (dto is null)
                return Result<ContentIdeaRecommendation>.Failure(ErrorCode.InternalError,
                    "Failed to parse AI response");

            var recommendations = dto.Recommendations
                .Where(r => Enum.TryParse<PlatformType>(r.Platform, true, out _)
                         && Enum.TryParse<ContentType>(r.Format, true, out _))
                .Select(r => new PlatformFormatOption(
                    Enum.Parse<PlatformType>(r.Platform, true),
                    Enum.Parse<ContentType>(r.Format, true),
                    r.SuggestedAngle,
                    r.Rationale,
                    r.ConfidenceScore))
                .OrderByDescending(r => r.ConfidenceScore)
                .ToList();

            return Result<ContentIdeaRecommendation>.Success(new ContentIdeaRecommendation(
                dto.StoryTitle,
                dto.StorySummary,
                sourceUrl,
                dto.Angles,
                recommendations));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse story idea recommendation JSON");
            return Result<ContentIdeaRecommendation>.Failure(ErrorCode.InternalError,
                "Failed to parse AI recommendation");
        }
    }

    private static string BuildPrompt(string storyText, string? sourceUrl)
    {
        var urlLine = sourceUrl is not null ? $"Source URL: {sourceUrl}\n" : string.Empty;

        return $$"""
            You are a personal brand strategist helping a technical content creator decide
            where and how to publish content about a story they've found.

            {{urlLine}}Story:
            {{storyText}}

            Analyze this story and return a JSON object (no markdown, no code fences) with this exact shape:

            {
              "storyTitle": "short descriptive title",
              "storySummary": "2-3 sentence summary of the key insight",
              "angles": [
                "Contrarian take: ...",
                "Teaching moment: ...",
                "Personal hook: ..."
              ],
              "recommendations": [
                {
                  "platform": "LinkedIn",
                  "format": "SocialPost",
                  "suggestedAngle": "Teaching moment",
                  "rationale": "LinkedIn professionals respond well to practical explainers on tooling...",
                  "confidenceScore": 0.88
                },
                {
                  "platform": "TwitterX",
                  "format": "Thread",
                  "suggestedAngle": "Contrarian take",
                  "rationale": "Hot take threads on dev tooling get strong traction in the AI/dev community...",
                  "confidenceScore": 0.82
                }
              ]
            }

            Rules:
            - platform must be one of: TwitterX, LinkedIn, Instagram, YouTube, Reddit
            - format must be one of: BlogPost, SocialPost, Thread, VideoDescription
            - confidenceScore is 0.0 to 1.0
            - Return 2-4 recommendations, ordered by fit
            - Return ONLY the JSON object, nothing else
            """;
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private async Task<(string? Text, string? Error)> ConsumeTextAsync(string prompt, CancellationToken ct)
    {
        string? lastSummary = null;
        try
        {
            await foreach (var evt in _sidecar.SendTaskAsync(prompt, null, null, ct))
            {
                switch (evt)
                {
                    case ChatEvent { EventType: "summary", Text: not null } chat:
                        lastSummary = chat.Text;
                        break;
                    case ErrorEvent err:
                        return (null, err.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consuming sidecar stream for story analysis");
            return (null, ex.Message);
        }

        return (lastSummary, null);
    }

    private record RecommendationDto(
        string StoryTitle,
        string StorySummary,
        List<string> Angles,
        List<PlatformFormatDto> Recommendations);

    private record PlatformFormatDto(
        string Platform,
        string Format,
        string SuggestedAngle,
        string Rationale,
        float ConfidenceScore);
}
