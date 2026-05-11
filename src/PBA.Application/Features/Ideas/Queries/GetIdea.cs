using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Ideas.Queries;

public static class GetIdea
{
    public record Query(Guid IdeaId) : IRequest<Result<IdeaDetailDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<IdeaDetailDto>>
    {
        public async Task<Result<IdeaDetailDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var idea = await db.Ideas
                .AsNoTracking()
                .Include(i => i.SavedDetails)
                .Include(i => i.IdeaSource)
                .FirstOrDefaultAsync(i => i.Id == request.IdeaId, cancellationToken);

            if (idea is null)
                return Result<IdeaDetailDto>.NotFound($"Idea {request.IdeaId} not found");

            List<IdeaConnectionDto>? connections = null;
            if (!string.IsNullOrWhiteSpace(idea.AIConnections))
            {
                try
                {
                    connections = JsonSerializer.Deserialize<List<IdeaConnectionDto>>(
                        idea.AIConnections,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException)
                {
                    // Malformed JSON -- return null connections rather than failing
                }
            }

            var dto = new IdeaDetailDto
            {
                Id = idea.Id,
                Title = idea.Title,
                SourceName = idea.SourceName,
                Category = idea.Category,
                Summary = idea.Summary,
                ThumbnailUrl = idea.ThumbnailUrl,
                Status = idea.Status,
                Tags = idea.Tags,
                DetectedAt = idea.DetectedAt,
                HasSavedDetails = idea.SavedDetails is not null,
                Description = idea.Description,
                Url = idea.Url,
                AIConnections = connections,
                SavedDetails = idea.SavedDetails is not null
                    ? new SavedIdeaDetailDto
                    {
                        Notes = idea.SavedDetails.Notes,
                        Tags = idea.SavedDetails.Tags,
                        SuggestedPlatforms = idea.SavedDetails.SuggestedPlatforms,
                        SuggestedAngle = idea.SavedDetails.SuggestedAngle,
                        SavedAt = idea.SavedDetails.SavedAt
                    }
                    : null,
                SourceInfo = idea.IdeaSource is not null
                    ? new IdeaSourceInfoDto
                    {
                        Name = idea.IdeaSource.Name,
                        Type = idea.IdeaSource.Type,
                        FeedUrl = idea.IdeaSource.FeedUrl
                    }
                    : null
            };

            return dto;
        }
    }
}
