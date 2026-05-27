namespace PBA.Application.Common.Interfaces;

using PBA.Application.Common.Models;
using PBA.Domain.Enums;

public interface IPlatformConnector
{
    Platform Platform { get; }
    Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct);
    Task<bool> ValidateCredentialsAsync(CancellationToken ct);
    PlatformCapabilities GetCapabilities();
}
