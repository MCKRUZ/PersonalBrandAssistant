using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Content.Queries;

public static class CheckVoice
{
    public record Query(Guid ContentId) : IRequest<Result<VoiceCheckDto>>;

    public sealed class Handler(IAppDbContext db, ISidecarClient sidecar) : IRequestHandler<Query, Result<VoiceCheckDto>>
    {
        public async Task<Result<VoiceCheckDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var content = await db.Contents.FindAsync([request.ContentId], cancellationToken);
            if (content is null)
                return Result<VoiceCheckDto>.NotFound($"Content {request.ContentId} not found");

            var profile = await db.BrandProfiles.FirstOrDefaultAsync(cancellationToken);

            var systemPrompt = BuildSystemPrompt(profile);
            var userPrompt = BuildUserPrompt(content.Body);

            var response = await sidecar.SendPromptAsync(systemPrompt, userPrompt, cancellationToken);

            if (!TryParseVoiceResponse(response, out var score, out var feedback))
                return Result<VoiceCheckDto>.Fail("Sidecar returned invalid voice check response");

            if (score < 0 || score > 100)
                return Result<VoiceCheckDto>.Fail("Voice score out of expected range (0-100)");

            content.VoiceScore = score;
            await db.SaveChangesAsync(cancellationToken);

            return Result<VoiceCheckDto>.Success(new VoiceCheckDto
            {
                Score = score,
                Feedback = feedback
            });
        }

        private static bool TryParseVoiceResponse(string response, out decimal score, out string feedback)
        {
            score = 0;
            feedback = string.Empty;

            var json = ExtractJsonObject(response);
            if (json is null)
                return false;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("score", out var scoreProp) ||
                    !doc.RootElement.TryGetProperty("feedback", out var feedbackProp))
                    return false;
                score = scoreProp.GetDecimal();
                feedback = feedbackProp.GetString() ?? string.Empty;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        // LLMs frequently wrap the JSON object in markdown fences or surrounding
        // prose ("Here is the analysis: {...}"). Extract the outermost object so
        // JsonDocument.Parse sees only the braces, not the chatter around them.
        private static string? ExtractJsonObject(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return null;

            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;

            return response[start..(end + 1)];
        }

        private static string BuildSystemPrompt(Domain.Entities.BrandProfile? profile)
        {
            if (profile is null)
                return "You are a brand voice analyst. Evaluate how well the content matches the brand's voice.";

            return $"""
                You are a brand voice analyst. Evaluate how well the content matches this brand profile:
                Personality: {profile.Personality}
                Tone: {profile.Tone}
                Vocabulary to use: {string.Join(", ", profile.Vocabulary)}
                Words to avoid: {string.Join(", ", profile.AvoidWords)}
                """;
        }

        private static string BuildUserPrompt(string body)
        {
            return $$"""
                Analyze this content for brand voice alignment:

                {{body}}

                Respond ONLY with JSON: {"score": <0-100>, "feedback": "<explanation>"}
                """;
        }
    }
}
