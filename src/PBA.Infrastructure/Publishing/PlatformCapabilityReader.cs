using Microsoft.Extensions.DependencyInjection;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Publishing;

/// <summary>
/// Reads capabilities straight off the registered connector, so the answer comes from the code that
/// actually does the publishing rather than a table that can drift away from it.
/// </summary>
public sealed class PlatformCapabilityReader(IServiceProvider serviceProvider) : IPlatformCapabilityReader
{
    // Null-safe on the capabilities too, not just the connector. Asking a question about a platform
    // must never be the thing that takes a publish down — false means "PBA keeps responsibility",
    // which is the safe answer in both directions: it never hands timing to something that will
    // ignore it, and never invents a pacing rule for a connector that did not ask for one.
    public bool SupportsScheduling(Platform platform) =>
        serviceProvider.GetKeyedService<IPlatformConnector>(platform)?
            .GetCapabilities()?.SupportsScheduling ?? false;

    public bool RequiresPacedHandover(Platform platform) =>
        serviceProvider.GetKeyedService<IPlatformConnector>(platform)?
            .GetCapabilities()?.RequiresPacedHandover ?? false;

    // Null, not false, is the neutral answer here: it means "no limit", so an unregistered or
    // silent connector keeps the behaviour every connector had before horizons existed.
    public TimeSpan? SchedulingHorizon(Platform platform) =>
        serviceProvider.GetKeyedService<IPlatformConnector>(platform)?
            .GetCapabilities()?.SchedulingHorizon;

    public bool FetchesHostedMediaAtPostTime(Platform platform) =>
        serviceProvider.GetKeyedService<IPlatformConnector>(platform)?
            .GetCapabilities()?.FetchesHostedMediaAtPostTime ?? false;
}
