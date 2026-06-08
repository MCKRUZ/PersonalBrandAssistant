using PBA.Domain.Common;

namespace PBA.Application.Common.Interfaces;

public enum DeliveryKind
{
    Digest,
    Alert
}

/// <summary>One ranked line in a delivery: a headline, its score, why it matters, and a link.</summary>
public sealed record DeliveryItem(int? Rank, int Score, string Headline, string WhyItMatters, string? Url);

/// <summary>
/// Transport-neutral payload pushed to external channels. A daily digest carries many items;
/// an instant alert carries one. Senders render this for their own medium.
/// </summary>
public sealed record DeliveryNotification(
    DeliveryKind Kind,
    string Title,
    string Intro,
    IReadOnlyList<DeliveryItem> Items);

/// <summary>
/// An external channel the radar can push to (email, Discord). Implementations are self-describing
/// (<see cref="Channel"/>) and self-gating (<see cref="IsEnabled"/>); <see cref="SendAsync"/> never
/// throws for an expected failure, it returns <see cref="Result.Fail"/>.
/// </summary>
public interface IDigestDeliverySender
{
    string Channel { get; }
    bool IsEnabled { get; }
    Task<Result> SendAsync(DeliveryNotification notification, CancellationToken ct = default);
}
