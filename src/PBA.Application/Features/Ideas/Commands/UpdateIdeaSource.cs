using MediatR;
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
}
