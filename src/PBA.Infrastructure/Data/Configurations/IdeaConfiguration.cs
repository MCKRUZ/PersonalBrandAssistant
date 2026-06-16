using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class IdeaConfiguration : IEntityTypeConfiguration<Idea>
{
    public void Configure(EntityTypeBuilder<Idea> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Title).IsRequired().HasMaxLength(500);
        builder.Property(i => i.Description).HasColumnType("text");
        builder.Property(i => i.Url).HasMaxLength(2000);
        builder.Property(i => i.SourceName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.ThumbnailUrl).HasMaxLength(2000);
        builder.Property(i => i.Category).HasMaxLength(100);
        builder.Property(i => i.Summary).HasColumnType("text");
        builder.Property(i => i.AIConnections).HasColumnType("text");
        builder.Property(i => i.Tags).HasColumnType("jsonb");
        builder.Property(i => i.DeduplicationKey).IsRequired().HasMaxLength(500);

        builder.HasOne(i => i.IdeaSource)
            .WithMany(s => (ICollection<Idea>)s.Ideas)
            .HasForeignKey(i => i.IdeaSourceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(i => i.IdeaSourceId);
        builder.HasIndex(i => i.DeduplicationKey);

        builder.Property(i => i.ScoreReason).HasColumnType("text");

        // Feed-ranking columns. PillarSubScores is a JSON document (NOT a join table): brandFit is
        // computed in the read handler in memory, never aggregated in SQL, so a relational table buys
        // nothing. Keyed by BrandPillarId (R-C3). A collection of a COMPLEX type isn't mappable by the
        // InMemory provider, so it goes through an explicit JSON string converter (stored as jsonb on
        // Npgsql, as a string on InMemory) rather than relying on Npgsql dynamic JSON. Embedding maps to
        // vector(1536) in PgVectorModelConfiguration (Npgsql-only).
        var subScoreConverter = new ValueConverter<IList<PillarSubScore>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<PillarSubScore>>(v, (JsonSerializerOptions?)null)
                 ?? new List<PillarSubScore>());
        var subScoreComparer = new ValueComparer<IList<PillarSubScore>>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                      == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<List<PillarSubScore>>(
                     JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!);
        // NOT NULL needs a store default so the migration can backfill the existing (~3,800-row) Ideas
        // table; new inserts always provide the serialized value (entity initializes to []). The
        // converter is the SOLE read/write path for this column, so default JsonSerializerOptions is
        // self-consistent (nothing hand-parses the raw jsonb).
        builder.Property(i => i.PillarSubScores)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(subScoreConverter, subScoreComparer);

        builder.Property(i => i.ScoreAttempts).HasDefaultValue(0);

        builder.HasIndex(i => i.ScoredAt);
        builder.HasIndex(i => i.Score);
        builder.HasIndex(i => i.DuplicateOfId);
        builder.HasIndex(i => i.AlertedAt);
        builder.HasIndex(i => i.ScoredProfileVersion);

        builder.HasOne<Idea>()
            .WithMany()
            .HasForeignKey(i => i.DuplicateOfId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
