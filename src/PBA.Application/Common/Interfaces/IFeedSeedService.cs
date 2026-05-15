namespace PBA.Application.Common.Interfaces;

public interface IFeedSeedService
{
    Task<int> SeedAsync(CancellationToken cancellationToken = default);
}
