using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

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

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(Command request, CancellationToken cancellationToken)
        {
            var deduplicationKey = DeduplicationKeyGenerator.Generate(request.Url, request.Title);

            var exists = await db.Ideas.AnyAsync(
                i => i.DeduplicationKey == deduplicationKey, cancellationToken);

            if (exists)
                return Result<Guid>.Fail("An idea with the same URL or title already exists");

            var idea = new Idea
            {
                Title = request.Title,
                Description = request.Description,
                Url = request.Url,
                Category = request.Category,
                Tags = request.Tags.ToList(),
                SourceName = "Manual",
                Status = IdeaStatus.New,
                DetectedAt = DateTimeOffset.UtcNow,
                DeduplicationKey = deduplicationKey
            };

            db.Ideas.Add(idea);
            await db.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(idea.Id);
        }
    }

    internal static string GenerateDeduplicationKey(string? url, string title)
        => DeduplicationKeyGenerator.Generate(url, title);
}
