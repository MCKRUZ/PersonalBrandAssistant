using System.Net.Http.Json;

namespace PBA.Infrastructure.Services.Scrapers;

internal static class HttpJsonExtensions
{
    /// <summary>Deserialize content, returning default on malformed JSON instead of throwing.</summary>
    public static async Task<T?> ReadFromJsonAsyncSafe<T>(this HttpContent content, CancellationToken ct)
    {
        try { return await content.ReadFromJsonAsync<T>(ct); }
        catch (System.Text.Json.JsonException) { return default; }
    }
}
