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
    /// <summary>
    /// How long a hosted object can be relied on to still be there. Storage reaps them on a
    /// lifecycle rule, and a platform that publishes from a URL fetches it LATER than it was
    /// uploaded — Buffer at the scheduled moment, which can be a week after PBA handed it the link.
    /// An object that is gone before it is fetched fails days later, in the middle of the night, as
    /// a dead link.
    ///
    /// Nothing is uploaded here while PBA is merely WAITING — a clip PBA holds lives beside its
    /// record until hand-over, so this window never has to cover the wait, only the gap between
    /// hand-over and the platform reading it. A connector whose gap could exceed this window
    /// reports it as its scheduling horizon so the planner can keep the hand-over inside it.
    /// </summary>
    TimeSpan MaxHostedLifetime { get; }

    Task<HostedMedia> UploadAsync(byte[] data, string fileName, string contentType, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>A hosted media object: its temporary public URL and the storage key that backs it.</summary>
public record HostedMedia(string Url, string Key);
