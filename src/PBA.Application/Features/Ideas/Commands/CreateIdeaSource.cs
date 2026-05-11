using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Commands;

public static class CreateIdeaSource
{
    public record Command : IRequest<Result<Guid>>
    {
        public string Name { get; init; } = string.Empty;
        public IdeaSourceType Type { get; init; }
        public string? FeedUrl { get; init; }
        public string? ApiUrl { get; init; }
        public string Category { get; init; } = string.Empty;
        public int PollIntervalMinutes { get; init; } = 30;
    }

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(Command request, CancellationToken cancellationToken)
        {
            var source = new IdeaSource
            {
                Name = request.Name,
                Type = request.Type,
                FeedUrl = request.FeedUrl,
                ApiUrl = request.ApiUrl,
                Category = request.Category,
                PollIntervalMinutes = request.PollIntervalMinutes
            };

            db.IdeaSources.Add(source);
            await db.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(source.Id);
        }
    }
}
