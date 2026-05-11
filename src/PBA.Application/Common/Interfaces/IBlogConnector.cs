using PBA.Domain.Entities;

namespace PBA.Application.Common.Interfaces;

public interface IBlogConnector
{
    Task<string> PublishAsync(Content content, CancellationToken ct);
}
