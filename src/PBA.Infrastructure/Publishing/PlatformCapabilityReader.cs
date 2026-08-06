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
    public bool SupportsScheduling(Platform platform) =>
        serviceProvider.GetKeyedService<IPlatformConnector>(platform)?
            .GetCapabilities().SupportsScheduling ?? false;
}
