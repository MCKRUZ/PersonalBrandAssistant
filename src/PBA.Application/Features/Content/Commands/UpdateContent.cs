using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Commands;

public static class UpdateContent
{
    public record Command(
        Guid ContentId,
        string? Title,
        string? Body,
        IReadOnlyList<string>? Tags,
        ContentType? ContentType,
        Platform? PrimaryPlatform,
        DateTimeOffset LastUpdatedAt) : IRequest<Result>;

    private static readonly HashSet<ContentStatus> EditableStatuses =
    [
        ContentStatus.Idea,
        ContentStatus.Draft,
        ContentStatus.Review,
    ];

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var content = await db.Contents.FindAsync([request.ContentId], cancellationToken);
            if (content is null)
                return Result.NotFound($"Content {request.ContentId} not found");

            if (!EditableStatuses.Contains(content.Status))
                return Result.Fail($"Cannot edit content in {content.Status} status");

            var timeDiff = (content.UpdatedAt - request.LastUpdatedAt).Duration();
            if (timeDiff > TimeSpan.FromMilliseconds(500))
                return Result.Conflict("Content was modified by another user. Please reload and try again.");

            if (request.Title is not null)
                content.Title = request.Title;

            if (request.Body is not null)
                content.Body = request.Body;

            if (request.Tags is not null)
                content.Tags = request.Tags.ToList();

            if (request.ContentType.HasValue)
                content.ContentType = request.ContentType.Value;

            if (request.PrimaryPlatform.HasValue)
                content.PrimaryPlatform = request.PrimaryPlatform.Value;

            content.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}
