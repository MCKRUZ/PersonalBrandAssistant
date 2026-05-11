using MediatR;
using PBA.Domain.Common;

namespace PBA.Application.Features.Ideas.Commands;

public static class CreateIdea
{
    public record Command : IRequest<Result<Guid>>
    {
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? Url { get; init; }
        public string? Category { get; init; }
        public IReadOnlyList<string> Tags { get; init; } = [];
    }
}
