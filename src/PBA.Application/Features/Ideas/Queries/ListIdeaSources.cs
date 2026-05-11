using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Ideas.Queries;

public static class ListIdeaSources
{
    public record Query : IRequest<Result<IReadOnlyList<IdeaSourceDto>>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<IReadOnlyList<IdeaSourceDto>>>
    {
        public async Task<Result<IReadOnlyList<IdeaSourceDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var sources = await db.IdeaSources
                .AsNoTracking()
                .Include(s => s.Ideas)
                .OrderBy(s => s.Name)
                .Select(s => new IdeaSourceDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Type = s.Type,
                    FeedUrl = s.FeedUrl,
                    ApiUrl = s.ApiUrl,
                    Category = s.Category,
                    PollIntervalMinutes = s.PollIntervalMinutes,
                    IsEnabled = s.IsEnabled,
                    LastPolledAt = s.LastPolledAt,
                    LastSuccessAt = s.LastSuccessAt,
                    LastError = s.LastError,
                    ConsecutiveFailures = s.ConsecutiveFailures,
                    IdeaCount = s.Ideas.Count,
                    IsHealthy = s.IsEnabled && s.ConsecutiveFailures < 3
                })
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<IdeaSourceDto>>.Success(sources);
        }
    }
}
