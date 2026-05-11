using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Dtos;
using PBA.Domain.Common;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Features.Content.Queries;

public static class GetContent
{
    public record Query(Guid ContentId) : IRequest<Result<ContentDetailDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<ContentDetailDto>>
    {
        public async Task<Result<ContentDetailDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var content = await db.Contents
                .AsNoTracking()
                .Include(c => c.CrossPosts)
                .FirstOrDefaultAsync(c => c.Id == request.ContentId, cancellationToken);

            if (content is null)
                return Result<ContentDetailDto>.NotFound($"Content {request.ContentId} not found");

            var children = await db.Contents
                .AsNoTracking()
                .Where(c => c.ParentContentId == request.ContentId)
                .Select(c => new ChildContentDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    ContentType = c.ContentType,
                    PrimaryPlatform = c.PrimaryPlatform,
                    Status = c.Status,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            return Result<ContentDetailDto>.Success(new ContentDetailDto
            {
                Id = content.Id,
                Title = content.Title,
                ContentType = content.ContentType,
                Status = content.Status,
                PrimaryPlatform = content.PrimaryPlatform,
                VoiceScore = content.VoiceScore,
                Tags = content.Tags,
                CreatedAt = content.CreatedAt,
                UpdatedAt = content.UpdatedAt,
                ScheduledAt = content.ScheduledAt,
                PublishedAt = content.PublishedAt,
                Body = content.Body,
                ViralityPrediction = content.ViralityPrediction,
                SourceIdeaId = content.SourceIdeaId,
                ParentContentId = content.ParentContentId,
                PlatformPublishes = content.CrossPosts.Select(cp => new PlatformPublishDto
                {
                    Id = cp.Id,
                    Platform = cp.Platform,
                    PublishStatus = cp.Status,
                    PublishedUrl = cp.PublishedUrl,
                    PublishedAt = cp.PublishedAt
                }).ToList(),
                Children = children
            });
        }
    }
}
