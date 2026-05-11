using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Features.Content.Commands;

public static class GenerateCrossPost
{
    public record Command(Guid ContentId, Platform TargetPlatform) : IRequest<Result<Guid>>;

    internal sealed class Handler(IAppDbContext db, ISidecarClient sidecar)
        : IRequestHandler<Command, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(Command request, CancellationToken cancellationToken)
        {
            var parent = await db.Contents.FindAsync([request.ContentId], cancellationToken);
            if (parent is null)
                return Result<Guid>.NotFound($"Content {request.ContentId} not found");

            if (request.TargetPlatform == parent.PrimaryPlatform)
                return Result<Guid>.Fail("Target platform must differ from source platform");

            var duplicateExists = await db.Contents.AnyAsync(
                c => c.ParentContentId == parent.Id
                     && c.PrimaryPlatform == request.TargetPlatform
                     && !c.IsDeleted,
                cancellationToken);

            if (duplicateExists)
                return Result<Guid>.Fail($"Cross-post to {request.TargetPlatform} already exists");

            var profile = await db.BrandProfiles.FirstOrDefaultAsync(cancellationToken);

            var systemPrompt = BuildSystemPrompt(profile);
            var userPrompt = BuildUserPrompt(parent, request.TargetPlatform);

            string response;
            try
            {
                response = await sidecar.SendPromptAsync(systemPrompt, userPrompt, cancellationToken);
            }
            catch (Exception ex)
            {
                return Result<Guid>.Fail($"AI cross-post generation failed: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(response))
                return Result<Guid>.Fail("AI returned empty content");

            var child = new ContentEntity
            {
                Title = parent.Title,
                Body = response,
                ContentType = parent.ContentType,
                PrimaryPlatform = request.TargetPlatform,
                Status = ContentStatus.Draft,
                ParentContentId = parent.Id,
                Tags = parent.Tags.ToList(),
            };

            db.Contents.Add(child);
            await db.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(child.Id);
        }

        private static string BuildSystemPrompt(Domain.Entities.BrandProfile? profile)
        {
            if (profile is null)
                return "You are a professional content writer. Adapt content for different platforms.";

            return $"""
                You are a professional content writer. Match this brand voice:
                Personality: {profile.Personality}
                Tone: {profile.Tone}
                Vocabulary to use: {string.Join(", ", profile.Vocabulary)}
                Words to avoid: {string.Join(", ", profile.AvoidWords)}
                Adapt the content for the target platform while maintaining voice consistency.
                """;
        }

        private static string BuildUserPrompt(ContentEntity parent, Platform targetPlatform)
        {
            var constraints = PlatformConstraints.GetConstraintDescription(targetPlatform);
            return $"""
                Adapt this {parent.ContentType} for {constraints}:

                Title: {parent.Title}
                <content>{parent.Body}</content>
                """;
        }
    }
}
