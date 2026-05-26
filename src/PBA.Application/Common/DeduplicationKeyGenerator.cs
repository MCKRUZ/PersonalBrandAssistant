using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace PBA.Application.Common;

public static class DeduplicationKeyGenerator
{
    private static readonly HashSet<string> TrackingParamPrefixes = ["utm_"];

    private static readonly HashSet<string> TrackingParams =
    [
        "fbclid", "gclid", "gclsrc", "mc_cid", "mc_eid",
        "ref", "source", "campaign_id", "ad_id",
        "msclkid", "twclid", "li_fat_id"
    ];

    public static string Generate(string? url, string title)
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
            .Where(k => k != null && IsTrackingParam(k))
            .ToList();

        foreach (var key in keysToRemove)
            query.Remove(key);

        var cleanQuery = query.ToString();
        var builder = new UriBuilder(uri)
        {
            Query = cleanQuery ?? string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Query).TrimEnd('/');
    }

    private static bool IsTrackingParam(string key)
    {
        var lower = key.ToLowerInvariant();
        return TrackingParams.Contains(lower)
            || TrackingParamPrefixes.Any(p => lower.StartsWith(p, StringComparison.Ordinal));
    }

    private static string HashSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
