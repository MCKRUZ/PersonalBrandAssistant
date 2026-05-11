using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Queries;

public static partial class GetIdeaConnections
{
    public record Query : IRequest<Result<IReadOnlyList<IdeaConnectionDto>>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<IReadOnlyList<IdeaConnectionDto>>>
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public async Task<Result<IReadOnlyList<IdeaConnectionDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var savedIdeas = await db.Ideas
                .AsNoTracking()
                .Where(i => i.Status == IdeaStatus.Saved && i.AIConnections != null)
                .Select(i => i.AIConnections!)
                .ToListAsync(cancellationToken);

            var connections = new List<IdeaConnectionDto>();

            foreach (var json in savedIdeas)
            {
                var cleaned = StripMarkdownCodeFences(json);

                try
                {
                    var parsed = JsonSerializer.Deserialize<List<IdeaConnectionDto>>(cleaned, JsonOptions);
                    if (parsed is not null)
                        connections.AddRange(parsed);
                }
                catch (JsonException)
                {
                    // Skip malformed JSON
                }
            }

            return Result<IReadOnlyList<IdeaConnectionDto>>.Success(connections);
        }

        private static string StripMarkdownCodeFences(string input)
        {
            var trimmed = input.Trim();
            if (!trimmed.StartsWith("```"))
                return trimmed;

            trimmed = CodeFenceStart().Replace(trimmed, string.Empty);
            trimmed = CodeFenceEnd().Replace(trimmed, string.Empty);
            return trimmed.Trim();
        }
    }

    [GeneratedRegex(@"^```\w*\s*", RegexOptions.Singleline)]
    private static partial Regex CodeFenceStart();

    [GeneratedRegex(@"\s*```\s*$", RegexOptions.Singleline)]
    private static partial Regex CodeFenceEnd();
}
