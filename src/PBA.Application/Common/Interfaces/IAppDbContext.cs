using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;

namespace PBA.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Content> Contents { get; }
    DbSet<ContentPlatformPublish> ContentPlatformPublishes { get; }
    DbSet<PlatformCredential> PlatformCredentials { get; }
    DbSet<BrandProfile> BrandProfiles { get; }
    DbSet<Idea> Ideas { get; }
    DbSet<SavedIdea> SavedIdeas { get; }
    DbSet<IdeaSource> IdeaSources { get; }
    DbSet<FeedItem> FeedItems { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
