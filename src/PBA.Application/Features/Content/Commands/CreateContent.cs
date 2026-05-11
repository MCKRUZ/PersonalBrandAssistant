using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Features.Content.Commands;

public static class CreateContent
{
    public record Command(
        string Title,
        ContentType ContentType,
        Platform PrimaryPlatform,
        Guid? SourceIdeaId,
        IReadOnlyList<string> Tags) : IRequest<Result<Guid>>;

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(Command request, CancellationToken cancellationToken)
        {
            var title = request.Title;
            var body = string.Empty;
            Guid? sourceIdeaId = null;

            if (request.SourceIdeaId.HasValue)
            {
                var idea = await db.Ideas.FindAsync([request.SourceIdeaId.Value], cancellationToken);
                if (idea is null)
                    return Result<Guid>.NotFound($"Idea {request.SourceIdeaId.Value} not found");

                if (string.IsNullOrWhiteSpace(title))
                    title = idea.Title;

                body = idea.Description ?? string.Empty;
                sourceIdeaId = idea.Id;
                idea.Status = IdeaStatus.Used;
            }

            var content = new ContentEntity
            {
                Title = title,
                Body = body,
                ContentType = request.ContentType,
                PrimaryPlatform = request.PrimaryPlatform,
                Status = ContentStatus.Idea,
                SourceIdeaId = sourceIdeaId,
                Tags = request.Tags.ToList(),
            };

            db.Contents.Add(content);
            await db.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(content.Id);
        }
    }
}
