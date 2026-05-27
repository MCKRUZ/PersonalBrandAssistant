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
    public DbSet<PlatformCredential> PlatformCredentials => Set<PlatformCredential>();
    public DbSet<BrandProfile> BrandProfiles => Set<BrandProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
