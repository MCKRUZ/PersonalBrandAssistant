namespace PBA.Domain.Enums;

/// <summary>
/// Which brief a <see cref="Entities.Digest"/> belongs to. Stored as int (Main=0) so existing rows
/// remain valid without a data backfill. One digest per (Date, Kind).
/// </summary>
public enum DigestKind
{
    /// <summary>The general brand-anchored brief over all sources.</summary>
    Main = 0,

    /// <summary>Pure-Microsoft brief: only items from Microsoft-owned sources.</summary>
    Microsoft = 1
}
