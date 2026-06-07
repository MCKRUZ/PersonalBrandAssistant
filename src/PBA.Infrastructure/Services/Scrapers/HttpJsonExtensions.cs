using System.Net.Http.Json;

namespace PBA.Infrastructure.Services.Scrapers;

internal static class HttpJsonExtensions
{
    /// <summary>GET + deserialize; returns null on a non-success status instead of throwing.</summary>
    public static async Task<T?> GetFromJsonOrNullAsync<T>(this HttpClient http, string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return default;
        return await resp.Content.ReadFromJsonAsync<T>(ct);
    }

    /// <summary>Deserialize content, returning default on malformed JSON instead of throwing.</summary>
    public static async Task<T?> ReadFromJsonAsyncSafe<T>(this HttpContent content, CancellationToken ct)
    {
        try { return await content.ReadFromJsonAsync<T>(ct); }
        catch (System.Text.Json.JsonException) { return default; }
    }
}
