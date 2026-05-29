namespace PBA.Application.Common.Interfaces;

public interface IIdeaSourceSeedService
{
    Task<int> SeedAsync(CancellationToken cancellationToken = default);
}
