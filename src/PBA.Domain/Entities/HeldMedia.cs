namespace PBA.Domain.Entities;

/// <summary>
/// The video bytes of a clip PBA is holding, kept until the moment PBA hands it to the platform.
///
/// It lives here rather than on public media storage because the two have different jobs. Public
/// storage exists so a platform that fetches by URL can reach the file, and it is reaped on a short
/// lifecycle rule for exactly that reason — nothing there is meant to be relied on for weeks. The
/// wait before a hand-over can be weeks, so putting the clip there at request time meant racing that
/// rule, and the whole reachable campaign length was decided by a storage setting rather than by
/// anything about the platform.
///
/// Keeping the bytes beside the record instead makes them survive as long as the record does, with
/// no second system to stay consistent with: they arrive in the same transaction that creates the
/// content and are removed in the one that finishes publishing it. There is no state where a clip
/// exists without its row, or a row points at bytes that were quietly reaped.
///
/// The public copy is made at hand-over, so the reaping window only has to cover hand-over → the
/// platform reading it, which is what that window was sized for in the first place.
/// </summary>
public class HeldMedia
{
    /// <summary>Primary key AND foreign key: a content record holds at most one clip.</summary>
    public Guid ContentId { get; set; }

    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public required byte[] Data { get; set; }

    public DateTimeOffset HeldAt { get; set; } = DateTimeOffset.UtcNow;
}
