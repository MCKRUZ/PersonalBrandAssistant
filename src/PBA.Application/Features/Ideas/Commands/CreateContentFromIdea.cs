using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Commands;

public static class CreateContentFromIdea
{
    public record Command(
        Guid IdeaId,
        ContentType ContentType,
        Platform PrimaryPlatform) : IRequest<Result<Guid>>;

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(Command request, CancellationToken cancellationToken)
        {
            var idea = await db.Ideas.FindAsync([request.IdeaId], cancellationToken);
            if (idea is null)
                return Result<Guid>.NotFound($"Idea {request.IdeaId} not found");

            var content = new Content
            {
                Title = idea.Title,
                Body = idea.Description ?? string.Empty,
                ContentType = request.ContentType,
                PrimaryPlatform = request.PrimaryPlatform,
                Status = ContentStatus.Idea,
                SourceIdeaId = idea.Id,
            };

            db.Contents.Add(content);
            idea.Status = IdeaStatus.Used;
            await db.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(content.Id);
        }
    }
}
