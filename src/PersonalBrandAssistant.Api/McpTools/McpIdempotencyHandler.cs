using Microsoft.Extensions.Caching.Memory;

namespace PersonalBrandAssistant.Api.McpTools;

public sealed class McpIdempotencyHandler
{
    private const string CacheKeyPrefix = "mcp:idempotency:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache;

    public McpIdempotencyHandler(IMemoryCache cache)
    {
        _cache = cache;
    }

    public string? TryGetCachedResult(string? clientRequestId)
    {
        if (string.IsNullOrEmpty(clientRequestId))
            return null;

        return _cache.TryGetValue($"{CacheKeyPrefix}{clientRequestId}", out string? cached)
            ? cached
            : null;
    }

    public void CacheResult(string? clientRequestId, string result)
    {
        if (string.IsNullOrEmpty(clientRequestId))
            return;

        _cache.Set(
            $"{CacheKeyPrefix}{clientRequestId}",
            result,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
    }
}
