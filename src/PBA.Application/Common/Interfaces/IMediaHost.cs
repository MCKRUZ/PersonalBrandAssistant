namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Hosts a transient media file at a public URL. Connectors that publish through a transport which
/// can only fetch media by URL (never raw bytes) — e.g. Buffer → TikTok — upload the bytes here
/// first and hand the returned URL to the transport. The URL is unauthenticated (Buffer HEAD-probes
/// it before accepting the post, which a signed single-method URL cannot satisfy); the object is
/// made un-guessable by a random key and reaped by a storage-side lifecycle rule.
/// </summary>
public interface IMediaHost
{
    Task<HostedMedia> UploadAsync(byte[] data, string fileName, string contentType, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>A hosted media object: its temporary public URL and the storage key that backs it.</summary>
public record HostedMedia(string Url, string Key);
