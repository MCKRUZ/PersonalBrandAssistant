using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Content> Contents => Set<Content>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<BrandProfile> BrandProfiles => Set<BrandProfile>();
    public DbSet<ContentCalendarSlot> ContentCalendarSlots => Set<ContentCalendarSlot>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
