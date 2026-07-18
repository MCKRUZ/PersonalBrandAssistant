using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class ChannelMetricSnapshotConfiguration : IEntityTypeConfiguration<ChannelMetricSnapshot>
{
    public void Configure(EntityTypeBuilder<ChannelMetricSnapshot> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Platform).HasConversion<int>();
        builder.Property(s => s.Scope).HasConversion<int>();

        // Npgsql maps DateOnly to `date` natively.
        builder.Property(s => s.SnapshotDate).IsRequired();

        builder.Property(s => s.VideoId).IsRequired().HasMaxLength(128);
        builder.Property(s => s.VideoTitle).HasMaxLength(500);

        // Metrics: a dictionary of a value type isn't mappable by the InMemory provider, so it goes through
        // an explicit JSON string converter (stored as jsonb on Npgsql, as a string on InMemory) — same
        // pattern as Idea.PillarSubScores. The converter is the sole read/write path, so default
        // JsonSerializerOptions is self-consistent.
        var metricsConverter = new ValueConverter<IReadOnlyDictionary<string, long>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, long>>(v, (JsonSerializerOptions?)null)
                 ?? new Dictionary<string, long>());
        // Comparison is key-order sensitive (System.Text.Json serializes a dictionary in insertion order),
        // so two logically-equal bags built in different order compare unequal. Acceptable here: snapshots
        // are write-once, so spurious change detection never fires. Mirrors IdeaConfiguration's comparer.
        var metricsComparer = new ValueComparer<IReadOnlyDictionary<string, long>>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                      == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<Dictionary<string, long>>(
                     JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!);
        builder.Property(s => s.Metrics)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(metricsConverter, metricsComparer);

        // Non-nullable VideoId => one Account row per platform-day (sentinel "") and one row per video-day.
        // Enables a real ON CONFLICT upsert in the poller (section-05).
        builder.HasIndex(s => new { s.Platform, s.SnapshotDate, s.Scope, s.VideoId })
            .IsUnique();

        // Range index for trend/range queries (section-06).
        builder.HasIndex(s => new { s.Platform, s.SnapshotDate });
    }
}
