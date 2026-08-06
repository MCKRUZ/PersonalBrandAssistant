using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Media;

/// <summary>
/// Hosts media on Cloudflare R2 (S3-compatible) and hands back its public custom-domain URL. Buffer
/// fetches the video from that URL — never raw bytes — and validates it with an unauthenticated HEAD
/// before accepting the post, so the URL must NOT be signed (a SigV4 presigned URL is bound to a
/// single HTTP method and 403s on HEAD). The object is instead served publicly under a random,
/// un-guessable key on a dedicated bucket.
/// </summary>
/// <remarks>
/// Cleanup is intentionally NOT inline: Buffer fetches the video asynchronously (well after
/// createPost returns, especially for scheduled posts), so deleting right after posting would race
/// that fetch. Expiry is handled storage-side by an R2 lifecycle rule on the bucket.
/// <see cref="DeleteAsync"/> stays on the interface for explicit future use but is never called on
/// the publish path.
/// </remarks>
/// <remarks>
/// The S3 client is taken lazily on purpose. AmazonS3Client validates its endpoint AT CONSTRUCTION
/// and throws when none is configured, so an eager dependency means a missing R2 setting fails to
/// build the whole publishing graph — including a blog or LinkedIn post that never touches a bucket.
/// Deferring construction to first actual use keeps a media-hosting misconfiguration contained to
/// the lanes that host media.
/// </remarks>
public sealed class R2MediaHost(Lazy<IAmazonS3> s3, IOptionsMonitor<R2Options> options) : IMediaHost
{
    public async Task<HostedMedia> UploadAsync(
        byte[] data, string fileName, string contentType, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var extension = Path.GetExtension(fileName);
        var key = $"{opts.KeyPrefix}{Guid.NewGuid():N}{extension}";

        using var stream = new MemoryStream(data);
        await s3.Value.PutObjectAsync(new PutObjectRequest
        {
            BucketName = opts.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            DisablePayloadSigning = true // R2 rejects streaming SigV4 payload signing; sign the header only.
        }, ct);

        var url = $"{opts.PublicBaseUrl.TrimEnd('/')}/{key}";
        return new HostedMedia(url, key);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        await s3.Value.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = opts.Bucket,
            Key = key
        }, ct);
    }
}
