using System.Security.Cryptography;
using System.Text;
using System.Web;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
            var deduplicationKey = GenerateDeduplicationKey(request.Url, request.Title);

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
                DeduplicationKey = deduplicationKey
            };

            db.Ideas.Add(idea);
            await db.SaveChangesAsync(cancellationToken);

            return Result<Guid>.Success(idea.Id);
        }
    }

    internal static string GenerateDeduplicationKey(string? url, string title)
    {
        var input = string.IsNullOrWhiteSpace(url)
            ? title.Trim().ToLowerInvariant()
            : NormalizeUrl(url);

        return HashSha256(input);
    }

    private static string NormalizeUrl(string url)
    {
        url = url.Trim().ToLowerInvariant().TrimEnd('/');

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var query = HttpUtility.ParseQueryString(uri.Query);
        var keysToRemove = query.AllKeys
            .Where(k => k != null && k.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
            query.Remove(key);

        var cleanQuery = query.ToString();
        var builder = new UriBuilder(uri) { Query = cleanQuery ?? string.Empty };
        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string HashSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
