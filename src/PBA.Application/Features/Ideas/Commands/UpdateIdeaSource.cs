using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;

namespace PBA.Application.Features.Ideas.Commands;

public static class UpdateIdeaSource
{
    public record Command : IRequest<Result>
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        public string? FeedUrl { get; init; }
        public string? ApiUrl { get; init; }
        public string? Category { get; init; }
        public int? PollIntervalMinutes { get; init; }
        public bool? IsEnabled { get; init; }
    }

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var source = await db.IdeaSources.FindAsync([request.Id], cancellationToken);

            if (source is null)
                return Result.NotFound($"IdeaSource {request.Id} not found");

            if (request.Name is not null) source.Name = request.Name;
            if (request.FeedUrl is not null) source.FeedUrl = request.FeedUrl;
            if (request.ApiUrl is not null) source.ApiUrl = request.ApiUrl;
            if (request.Category is not null) source.Category = request.Category;
            if (request.PollIntervalMinutes.HasValue) source.PollIntervalMinutes = request.PollIntervalMinutes.Value;
            if (request.IsEnabled.HasValue) source.IsEnabled = request.IsEnabled.Value;

            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}
