namespace PBA.Infrastructure.Configuration;

public sealed class R2Options
{
    public const string SectionName = "Media:R2";

    public bool Enabled { get; init; }
    public required string AccessKeyId { get; init; }
    public required string SecretAccessKey { get; init; }

    /// <summary>S3-compatible API endpoint used to upload objects (…r2.cloudflarestorage.com).</summary>
    public required string Endpoint { get; init; }

    /// <summary>
    /// Public base URL the object is served from — the bucket's custom domain
    /// (e.g. https://media.matthewkruczek.ai). This is what Buffer fetches; it must answer an
    /// unauthenticated GET *and* HEAD, so it cannot be a signed URL.
    /// </summary>
    public required string PublicBaseUrl { get; init; }

    public string Bucket { get; init; } = "pba-media";
    public string KeyPrefix { get; init; } = "tiktok/";
}
