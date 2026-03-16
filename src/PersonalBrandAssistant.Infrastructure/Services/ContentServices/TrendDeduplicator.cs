using System.Security.Cryptography;
using System.Text;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

internal static class TrendDeduplicator
{
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term",
        "ref", "source", "fbclid", "gclid",
    };

    public static string CanonicalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return url.Trim().ToLowerInvariant();

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var keysToRemove = query.AllKeys
            .Where(k => k is not null && (TrackingParams.Contains(k) || k.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var key in keysToRemove)
            query.Remove(key);

        var builder = new UriBuilder(uri)
        {
            Query = query.Count > 0 ? query.ToString() : string.Empty,
            Fragment = string.Empty,
        };

        var canonical = builder.Uri.GetLeftPart(UriPartial.Query).ToLowerInvariant();
        return canonical.TrimEnd('/');
    }

    public static string ComputeDeduplicationKey(string canonicalUrl)
    {
        if (string.IsNullOrWhiteSpace(canonicalUrl))
            return string.Empty;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalUrl));
        return Convert.ToHexStringLower(bytes);
    }

    public static float ComputeTitleSimilarity(string title1, string title2)
    {
        if (string.IsNullOrWhiteSpace(title1) || string.IsNullOrWhiteSpace(title2))
            return 0f;

        var words1 = Tokenize(title1);
        var words2 = Tokenize(title2);

        if (words1.Count == 0 && words2.Count == 0)
            return 1f;
        if (words1.Count == 0 || words2.Count == 0)
            return 0f;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union == 0 ? 0f : (float)intersection / union;
    }

    public static IReadOnlyList<TrendItem> Deduplicate(
        IEnumerable<TrendItem> items, float titleSimilarityThreshold)
    {
        var itemList = items.ToList();
        var result = new List<TrendItem>();
        var usedIndices = new HashSet<int>();

        // First pass: group by URL deduplication key (no mutation of input entities)
        var urlGroups = new Dictionary<string, List<int>>();
        for (var i = 0; i < itemList.Count; i++)
        {
            var item = itemList[i];
            if (!string.IsNullOrWhiteSpace(item.Url))
            {
                var canonical = CanonicalizeUrl(item.Url);
                var key = ComputeDeduplicationKey(canonical);

                if (!urlGroups.TryGetValue(key, out var group))
                {
                    group = [];
                    urlGroups[key] = group;
                }
                group.Add(i);
            }
        }

        // Merge URL-based duplicates: keep earliest DetectedAt
        foreach (var group in urlGroups.Values)
        {
            var best = group.OrderBy(i => itemList[i].DetectedAt).First();
            result.Add(itemList[best]);
            foreach (var idx in group)
                usedIndices.Add(idx);
        }

        // Second pass: items without URLs, compare titles against existing results
        for (var i = 0; i < itemList.Count; i++)
        {
            if (usedIndices.Contains(i))
                continue;

            var item = itemList[i];
            var isDuplicate = result.Any(existing =>
                ComputeTitleSimilarity(item.Title, existing.Title) >= titleSimilarityThreshold);

            if (!isDuplicate)
                result.Add(item);
        }

        return result;
    }

    private static HashSet<string> Tokenize(string text)
    {
        return text
            .ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r', ',', '.', '!', '?', ':', ';', '-', '(', ')', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
    }
}
