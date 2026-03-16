using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Content> Contents { get; }
    DbSet<Platform> Platforms { get; }
    DbSet<BrandProfile> BrandProfiles { get; }
    DbSet<CalendarSlot> CalendarSlots { get; }
    DbSet<ContentSeries> ContentSeries { get; }
    DbSet<TrendSource> TrendSources { get; }
    DbSet<TrendItem> TrendItems { get; }
    DbSet<TrendSuggestion> TrendSuggestions { get; }
    DbSet<TrendSuggestionItem> TrendSuggestionItems { get; }
    DbSet<EngagementSnapshot> EngagementSnapshots { get; }
    DbSet<AuditLogEntry> AuditLogEntries { get; }
    DbSet<User> Users { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<WorkflowTransitionLog> WorkflowTransitionLogs { get; }
    DbSet<AutonomyConfiguration> AutonomyConfigurations { get; }
    DbSet<AgentExecution> AgentExecutions { get; }
    DbSet<AgentExecutionLog> AgentExecutionLogs { get; }
    DbSet<ContentPlatformStatus> ContentPlatformStatuses { get; }
    DbSet<OAuthState> OAuthStates { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
