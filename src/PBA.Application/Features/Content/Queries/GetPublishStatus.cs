using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Content.Queries;

public static class GetPublishStatus
{
    public record Query(Guid ContentId) : IRequest<Result<PublishStatusDto>>;

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<PublishStatusDto>>
    {
        public async Task<Result<PublishStatusDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var contentExists = await db.Contents.AnyAsync(c => c.Id == request.ContentId, cancellationToken);
            if (!contentExists)
                return Result<PublishStatusDto>.NotFound($"Content {request.ContentId} not found");

            var publishes = await db.ContentPlatformPublishes
                .Where(p => p.ContentId == request.ContentId)
                .Select(p => new PlatformPublishDto
                {
                    Id = p.Id,
                    Platform = p.Platform,
                    PublishStatus = p.Status,
                    PublishedUrl = p.PublishedUrl,
                    PublishedAt = p.PublishedAt,
                    ErrorMessage = p.ErrorMessage,
                    RetryCount = p.RetryCount,
                    NextRetryAt = p.NextRetryAt
                })
                .ToListAsync(cancellationToken);

            return new PublishStatusDto
            {
                ContentId = request.ContentId,
                Platforms = publishes
            };
        }
    }
}
