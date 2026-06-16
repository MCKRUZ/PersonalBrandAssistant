using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;

namespace PBA.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Content> Contents { get; }
    DbSet<ContentPlatformPublish> ContentPlatformPublishes { get; }
    DbSet<PlatformCredential> PlatformCredentials { get; }
    DbSet<BrandProfile> BrandProfiles { get; }
    DbSet<BrandRankingProfile> BrandRankingProfiles { get; }
    DbSet<Idea> Ideas { get; }
    DbSet<SavedIdea> SavedIdeas { get; }
    DbSet<IdeaSource> IdeaSources { get; }
    DbSet<FeedItem> FeedItems { get; }
    DbSet<Digest> Digests { get; }
    DbSet<DigestItem> DigestItems { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the change-tracker ORIGINAL value of a tracked entity's property — used to seed an
    /// optimistic-concurrency token (e.g. the xmin token, whose setter is private) with the client's value
    /// so EF detects a stale token. A narrow port: keeps EF's change-tracker surface in Infrastructure
    /// rather than exposing it to the whole application layer.</summary>
    void SetOriginalValue<TEntity>(TEntity entity, string propertyName, object value) where TEntity : class;
}
