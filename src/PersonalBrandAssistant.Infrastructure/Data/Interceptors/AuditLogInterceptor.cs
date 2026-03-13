using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Interceptors;

public class AuditLogInterceptor : SaveChangesInterceptor
{
    private const int MaxValueLength = 4096;
    private static readonly HashSet<string> ExcludedPatterns =
        ["Encrypted", "Token", "Password", "Secret"];

    private readonly IDateTimeProvider _dateTimeProvider;

    public AuditLogInterceptor(IDateTimeProvider dateTimeProvider)
    {
        _dateTimeProvider = dateTimeProvider;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            CreateAuditEntries(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void CreateAuditEntries(DbContext context)
    {
        var entries = context.ChangeTracker.Entries<EntityBase>()
            .Where(e => e.Entity is not AuditLogEntry)
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var now = _dateTimeProvider.UtcNow;

        foreach (var entry in entries)
        {
            var auditEntry = new AuditLogEntry
            {
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entry.Entity.Id,
                Action = entry.State switch
                {
                    EntityState.Added => "Created",
                    EntityState.Modified => "Modified",
                    EntityState.Deleted => "Deleted",
                    _ => "Unknown"
                },
                Timestamp = now,
                OldValue = entry.State is EntityState.Modified or EntityState.Deleted
                    ? Truncate(SerializeValues(entry, useOriginal: true))
                    : null,
                NewValue = entry.State is EntityState.Added or EntityState.Modified
                    ? Truncate(SerializeValues(entry, useOriginal: false))
                    : null,
            };

            context.Set<AuditLogEntry>().Add(auditEntry);
        }
    }

    private static string SerializeValues(EntityEntry entry, bool useOriginal)
    {
        var values = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            var propertyName = property.Metadata.Name;

            if (ExcludedPatterns.Any(p => propertyName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            values[propertyName] = useOriginal
                ? property.OriginalValue
                : property.CurrentValue;
        }

        return JsonSerializer.Serialize(values);
    }

    private static string? Truncate(string? value)
    {
        if (value is null) return null;
        return value.Length > MaxValueLength ? value[..MaxValueLength] : value;
    }
}
