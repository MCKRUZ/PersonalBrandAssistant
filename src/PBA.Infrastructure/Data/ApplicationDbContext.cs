using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IAppDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Content> Contents => Set<Content>();
    public DbSet<ContentPlatformPublish> ContentPlatformPublishes => Set<ContentPlatformPublish>();
    public DbSet<IdeaSource> IdeaSources => Set<IdeaSource>();
    public DbSet<Idea> Ideas => Set<Idea>();
    public DbSet<SavedIdea> SavedIdeas => Set<SavedIdea>();
    public DbSet<FeedItem> FeedItems => Set<FeedItem>();
    public DbSet<Digest> Digests => Set<Digest>();
    public DbSet<DigestItem> DigestItems => Set<DigestItem>();
    public DbSet<PlatformCredential> PlatformCredentials => Set<PlatformCredential>();
    public DbSet<BrandProfile> BrandProfiles => Set<BrandProfile>();
    public DbSet<BrandRankingProfile> BrandRankingProfiles => Set<BrandRankingProfile>();
    public DbSet<ChannelMetricSnapshot> ChannelMetricSnapshots => Set<ChannelMetricSnapshot>();
    public DbSet<HeldMedia> HeldMedia => Set<HeldMedia>();

    public void SetOriginalValue<TEntity>(TEntity entity, string propertyName, object value)
        where TEntity : class
        => Entry(entity).Property(propertyName).OriginalValue = value;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // pgvector mappings (vector extension + vector(1536) columns) only under the Npgsql provider;
        // the InMemory test provider can't map the Vector provider type.
        if (Database.IsNpgsql())
        {
            PgVectorModelConfiguration.Apply(modelBuilder);
        }

        base.OnModelCreating(modelBuilder);
    }
}
