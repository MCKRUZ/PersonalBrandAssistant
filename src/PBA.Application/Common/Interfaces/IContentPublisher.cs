using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

public interface IContentPublisher
{
    Task PublishAsync(Guid contentId);

    Task<PublishResult> PublishAsync(Guid contentId, IReadOnlyList<Platform>? targetPlatforms, CancellationToken ct);
}
