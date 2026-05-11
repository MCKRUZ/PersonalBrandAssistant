using MediatR;
using PBA.Domain.Common;

namespace PBA.Application.Features.Ideas.Commands;

public static class SaveIdea
{
    public record Command : IRequest<Result>
    {
        public Guid IdeaId { get; init; }
        public string? Notes { get; init; }
        public IReadOnlyList<string> Tags { get; init; } = [];
    }
}
