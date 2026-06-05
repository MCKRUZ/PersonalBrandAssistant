using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Dtos;
using PBA.Application.Features.Content.Mappings;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Commands;

public static class DraftContent
{
    public record Command(
        Guid ContentId,
        string Action,
        string? Instructions,
        string? ToneName) : IRequest<Result<ContentDetailDto>>;

    internal sealed class Handler(IAppDbContext db, ISidecarClient sidecar)
        : IRequestHandler<Command, Result<ContentDetailDto>>
    {
        public async Task<Result<ContentDetailDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var content = await db.Contents.FindAsync([request.ContentId], cancellationToken);
            if (content is null)
                return Result<ContentDetailDto>.NotFound($"Content {request.ContentId} not found");

            var profile = await db.BrandProfiles.FirstOrDefaultAsync(cancellationToken);

            var systemPrompt = BuildSystemPrompt(profile);
            var userPrompt = BuildUserPrompt(request, content);

            string response;
            try
            {
                response = await sidecar.SendPromptAsync(systemPrompt, userPrompt, ct: cancellationToken);
            }
            catch (Exception ex)
            {
                return Result<ContentDetailDto>.Fail($"AI drafting failed: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(response))
                return Result<ContentDetailDto>.Fail("AI returned empty content");

            if (content.Status == ContentStatus.Idea)
            {
                var machine = ContentStateMachine.Create(content);
                await machine.FireAsync(ContentTrigger.StartDraft);
            }

            content.Body = response;
            content.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result<ContentDetailDto>.Success(content.ToDetailDto());
        }

        private static string BuildSystemPrompt(Domain.Entities.BrandProfile? profile)
        {
            if (profile is null)
                return "You are a professional content writer. Write clear, engaging content.";

            return $"""
                You are a professional content writer. Match this brand voice:
                Personality: {profile.Personality}
                Tone: {profile.Tone}
                Vocabulary to use: {string.Join(", ", profile.Vocabulary)}
                Words to avoid: {string.Join(", ", profile.AvoidWords)}
                Never use em dashes. Write in a natural, human voice.
                """;
        }

        private static string BuildUserPrompt(Command request, Domain.Entities.Content content)
        {
            var platform = PlatformConstraints.GetConstraintDescription(content.PrimaryPlatform);
            var instructions = string.IsNullOrWhiteSpace(request.Instructions)
                ? ""
                : $"\n\nAdditional instructions: {request.Instructions}";

            var basePrompt = request.Action.ToLowerInvariant() switch
            {
                "draft" => $"Generate a {content.ContentType} for {platform}. Topic: {content.Title}.\n<content>{content.Body}</content>",
                "refine" => $"Improve this {content.ContentType}:\n<content>{content.Body}</content>",
                "shorten" => $"Shorten this to fit {platform} constraints:\n<content>{content.Body}</content>",
                "expand" => $"Expand this {content.ContentType} with more detail:\n<content>{content.Body}</content>",
                "changetone" => $"Rewrite in a {request.ToneName} tone:\n<content>{content.Body}</content>",
                _ => throw new ArgumentOutOfRangeException(nameof(request.Action), $"Unknown draft action: {request.Action}"),
            };

            return basePrompt + instructions;
        }
    }
}
