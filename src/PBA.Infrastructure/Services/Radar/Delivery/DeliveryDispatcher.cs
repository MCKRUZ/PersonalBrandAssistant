using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;

namespace PBA.Infrastructure.Services.Radar.Delivery;

public sealed class DeliveryDispatcher(
    IEnumerable<IDigestDeliverySender> senders,
    ILogger<DeliveryDispatcher> logger) : IDeliveryDispatcher
{
    public async Task DispatchAsync(DeliveryNotification notification, CancellationToken ct = default)
    {
        foreach (var sender in senders)
        {
            if (!sender.IsEnabled) continue;

            try
            {
                var result = await sender.SendAsync(notification, ct);
                if (!result.IsSuccess)
                    logger.LogWarning("Delivery channel {Channel} reported failure: {Errors}",
                        sender.Channel, string.Join("; ", result.Errors));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Delivery channel {Channel} threw while sending", sender.Channel);
            }
        }
    }
}
