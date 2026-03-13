using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Content> Contents { get; }
    DbSet<Platform> Platforms { get; }
    DbSet<BrandProfile> BrandProfiles { get; }
    DbSet<ContentCalendarSlot> ContentCalendarSlots { get; }
    DbSet<AuditLogEntry> AuditLogEntries { get; }
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
