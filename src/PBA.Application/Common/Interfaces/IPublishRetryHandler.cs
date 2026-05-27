namespace PBA.Application.Common.Interfaces;

public interface IPublishRetryHandler
{
    Task RetryAsync(Guid publishRecordId, CancellationToken ct = default);
}
