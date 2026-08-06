using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Finds the earliest moment a platform will accept another upload, for platforms that ration them.
/// Registered per platform by keyed DI; a platform without one is not rationed.
/// </summary>
public interface IUploadPacer
{
    Platform Platform { get; }

    /// <summary>
    /// The earliest handover time that still leaves room in the budget, or null when every day
    /// between now and <paramref name="goLiveAt"/> is already spoken for. Returning null must mean
    /// "cannot", never "probably fine" — the caller turns it into a refusal the operator can see,
    /// which is the whole point of asking before accepting the clip.
    /// </summary>
    Task<DateTimeOffset?> NextHandoverSlotAsync(DateTimeOffset goLiveAt, CancellationToken ct);
}
