using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Commands;

public static class RefreshIdeaSources
{
    public record Command : IRequest<Result<int>>;

    internal sealed class Handler(
        IAppDbContext db,
        IRssFeedReader feedReader,
        ILogger<Handler> logger) : IRequestHandler<Command, Result<int>>
    {
        public async Task<Result<int>> Handle(Command request, CancellationToken cancellationToken)
        {
            var sources = await db.IdeaSources
                .Where(s => s.IsEnabled && s.FeedUrl != null && s.FeedUrl != "")
                .ToListAsync(cancellationToken);

            var newIdeaCount = 0;

            foreach (var source in sources)
            {
                try
                {
                    var entries = await feedReader.ReadFeedAsync(
                        source.FeedUrl!, cancellationToken);

                    foreach (var entry in entries)
                    {
                        var dedupKey = CreateIdea.GenerateDeduplicationKey(entry.Url, entry.Title);

                        var exists = await db.Ideas.AnyAsync(
                            i => i.DeduplicationKey == dedupKey, cancellationToken);

                        if (exists)
                            continue;

                        db.Ideas.Add(new Idea
                        {
                            Title = entry.Title,
                            Description = entry.Description,
                            Url = entry.Url,
                            SourceName = source.Name,
                            IdeaSourceId = source.Id,
                            ThumbnailUrl = entry.ThumbnailUrl,
                            Category = entry.Category ?? source.Category,
                            Status = IdeaStatus.New,
                            DeduplicationKey = dedupKey
                        });

                        newIdeaCount++;
                    }

                    source.LastPolledAt = DateTimeOffset.UtcNow;
                    source.LastSuccessAt = DateTimeOffset.UtcNow;
                    source.ConsecutiveFailures = 0;
                    source.LastError = null;
                }
                catch (Exception ex)
                {
                    source.ConsecutiveFailures++;
                    source.LastError = ex.Message;
                    logger.LogWarning(ex, "Failed to refresh source {SourceName} ({SourceId})",
                        source.Name, source.Id);
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            return Result<int>.Success(newIdeaCount);
        }
    }
}
