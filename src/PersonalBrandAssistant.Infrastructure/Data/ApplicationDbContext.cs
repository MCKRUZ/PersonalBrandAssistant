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
    public DbSet<CalendarSlot> CalendarSlots => Set<CalendarSlot>();
    public DbSet<ContentSeries> ContentSeries => Set<ContentSeries>();
    public DbSet<TrendSource> TrendSources => Set<TrendSource>();
    public DbSet<TrendItem> TrendItems => Set<TrendItem>();
    public DbSet<TrendSuggestion> TrendSuggestions => Set<TrendSuggestion>();
    public DbSet<TrendSuggestionItem> TrendSuggestionItems => Set<TrendSuggestionItem>();
    public DbSet<EngagementSnapshot> EngagementSnapshots => Set<EngagementSnapshot>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<WorkflowTransitionLog> WorkflowTransitionLogs => Set<WorkflowTransitionLog>();
    public DbSet<AutonomyConfiguration> AutonomyConfigurations => Set<AutonomyConfiguration>();
    public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();
    public DbSet<AgentExecutionLog> AgentExecutionLogs => Set<AgentExecutionLog>();
    public DbSet<ContentPlatformStatus> ContentPlatformStatuses => Set<ContentPlatformStatus>();
    public DbSet<OAuthState> OAuthStates => Set<OAuthState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
