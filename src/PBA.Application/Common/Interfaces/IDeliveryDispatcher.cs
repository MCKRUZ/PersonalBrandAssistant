namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Fans a <see cref="DeliveryNotification"/> out to every enabled <see cref="IDigestDeliverySender"/>,
/// isolating each send so one failing channel never blocks another or breaks the caller.
/// </summary>
public interface IDeliveryDispatcher
{
    Task DispatchAsync(DeliveryNotification notification, CancellationToken ct = default);
}
