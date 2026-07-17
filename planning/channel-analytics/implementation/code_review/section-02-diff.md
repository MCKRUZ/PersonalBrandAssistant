diff --git a/scripts/migrate-add-channel-analytics.sql b/scripts/migrate-add-channel-analytics.sql
new file mode 100644
index 0000000..8d5b8fb
--- /dev/null
+++ b/scripts/migrate-add-channel-analytics.sql
@@ -0,0 +1,49 @@
+START TRANSACTION;
+
+DO $EF$
+BEGIN
+    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260717191429_AddChannelAnalytics') THEN
+
+    -- 1. Drop the old single-column unique index so a re-apply never leaves both indexes side by side.
+    DROP INDEX IF EXISTS "IX_PlatformCredentials_Platform";
+
+    -- 2. Purpose discriminator on existing credentials (0 = Publishing, back-compat for every existing row).
+    ALTER TABLE "PlatformCredentials" ADD COLUMN IF NOT EXISTS "Purpose" integer NOT NULL DEFAULT 0;
+
+    -- 3. New composite filtered unique index: one active credential per (Platform, Purpose).
+    CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlatformCredentials_Platform_Purpose"
+        ON "PlatformCredentials" ("Platform", "Purpose")
+        WHERE "IsActive" = true;
+
+    -- 4. Cumulative daily metric snapshots.
+    CREATE TABLE IF NOT EXISTS "ChannelMetricSnapshots" (
+        "Id" uuid NOT NULL,
+        "Platform" integer NOT NULL,
+        "SnapshotDate" date NOT NULL,
+        "Scope" integer NOT NULL,
+        "VideoId" character varying(128) NOT NULL,
+        "VideoTitle" character varying(500) NULL,
+        "Metrics" jsonb NOT NULL,
+        "CapturedAt" timestamp with time zone NOT NULL,
+        CONSTRAINT "PK_ChannelMetricSnapshots" PRIMARY KEY ("Id")
+    );
+
+    -- 5a. Unique index: one Account row per platform-day (VideoId sentinel "") and one row per video-day.
+    CREATE UNIQUE INDEX IF NOT EXISTS "IX_ChannelMetricSnapshots_Platform_SnapshotDate_Scope_VideoId"
+        ON "ChannelMetricSnapshots" ("Platform", "SnapshotDate", "Scope", "VideoId");
+
+    -- 5b. Range index for trend/range queries.
+    CREATE INDEX IF NOT EXISTS "IX_ChannelMetricSnapshots_Platform_SnapshotDate"
+        ON "ChannelMetricSnapshots" ("Platform", "SnapshotDate");
+
+    END IF;
+END $EF$;
+
+DO $EF$
+BEGIN
+    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260717191429_AddChannelAnalytics') THEN
+    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
+    VALUES ('20260717191429_AddChannelAnalytics', '10.0.7');
+    END IF;
+END $EF$;
+COMMIT;
diff --git a/src/PBA.Application/Common/Interfaces/IAppDbContext.cs b/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
index 2d2b5ae..c1452b8 100644
--- a/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
+++ b/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
@@ -16,6 +16,7 @@ public interface IAppDbContext
     DbSet<FeedItem> FeedItems { get; }
     DbSet<Digest> Digests { get; }
     DbSet<DigestItem> DigestItems { get; }
+    DbSet<ChannelMetricSnapshot> ChannelMetricSnapshots { get; }
     Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
 
     /// <summary>Sets the change-tracker ORIGINAL value of a tracked entity's property — used to seed an
diff --git a/src/PBA.Domain/Entities/ChannelMetricSnapshot.cs b/src/PBA.Domain/Entities/ChannelMetricSnapshot.cs
new file mode 100644
index 0000000..0b958f7
--- /dev/null
+++ b/src/PBA.Domain/Entities/ChannelMetricSnapshot.cs
@@ -0,0 +1,29 @@
+namespace PBA.Domain.Entities;
+
+using PBA.Domain.Enums;
+
+// A daily cumulative capture of channel metrics. Stores only cumulative, as-of-capture integer counts;
+// all trends (growth, gained/lost) are derived at read time (section-06) as deltas between snapshots.
+public class ChannelMetricSnapshot
+{
+    public Guid Id { get; init; } = Guid.NewGuid();
+    public Platform Platform { get; init; }
+
+    // Host-LOCAL capture date (same clock as ChannelAnalytics:RunAtLocalTime in the poller).
+    public DateOnly SnapshotDate { get; init; }
+
+    public SnapshotScope Scope { get; init; }
+
+    // NON-NULLABLE. Sentinel "" for Account scope so the (Platform, SnapshotDate, Scope, VideoId) unique
+    // index + the poller's ON CONFLICT upsert work (PostgreSQL treats NULLs as distinct).
+    public string VideoId { get; init; } = string.Empty;
+
+    // Denormalized for display (Video scope only).
+    public string? VideoTitle { get; init; }
+
+    // metric name -> cumulative integer value (or whole seconds); stored as jsonb. No fractions ever.
+    public IReadOnlyDictionary<string, long> Metrics { get; init; }
+        = new Dictionary<string, long>();
+
+    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
+}
diff --git a/src/PBA.Domain/Entities/PlatformCredential.cs b/src/PBA.Domain/Entities/PlatformCredential.cs
index 1f8bd9f..deac4ca 100644
--- a/src/PBA.Domain/Entities/PlatformCredential.cs
+++ b/src/PBA.Domain/Entities/PlatformCredential.cs
@@ -6,6 +6,10 @@ public class PlatformCredential
 {
     public Guid Id { get; init; } = Guid.NewGuid();
     public Platform Platform { get; init; }
+
+    // Publishing (default, preserves the meaning of every existing row) vs Analytics. Lets one active
+    // Publishing token and one active Analytics token coexist per platform.
+    public CredentialPurpose Purpose { get; init; } = CredentialPurpose.Publishing;
     public string EncryptedAccessToken { get; set; } = string.Empty;
     public string? EncryptedRefreshToken { get; set; }
     public DateTimeOffset? AccessTokenExpiresAt { get; set; }
diff --git a/src/PBA.Domain/Enums/CredentialPurpose.cs b/src/PBA.Domain/Enums/CredentialPurpose.cs
new file mode 100644
index 0000000..41d71ad
--- /dev/null
+++ b/src/PBA.Domain/Enums/CredentialPurpose.cs
@@ -0,0 +1,7 @@
+namespace PBA.Domain.Enums;
+
+public enum CredentialPurpose
+{
+    Publishing = 0,
+    Analytics = 1
+}
diff --git a/src/PBA.Domain/Enums/Platform.cs b/src/PBA.Domain/Enums/Platform.cs
index e15e5fd..d83065c 100644
--- a/src/PBA.Domain/Enums/Platform.cs
+++ b/src/PBA.Domain/Enums/Platform.cs
@@ -8,5 +8,7 @@ public enum Platform
     Twitter = 3,
     Reddit = 4,
     YouTube = 5,
-    Medium = 6
+    Medium = 6,
+    Instagram = 7,
+    TikTok = 8
 }
diff --git a/src/PBA.Domain/Enums/SnapshotScope.cs b/src/PBA.Domain/Enums/SnapshotScope.cs
new file mode 100644
index 0000000..20596ec
--- /dev/null
+++ b/src/PBA.Domain/Enums/SnapshotScope.cs
@@ -0,0 +1,7 @@
+namespace PBA.Domain.Enums;
+
+public enum SnapshotScope
+{
+    Account = 0,
+    Video = 1
+}
diff --git a/src/PBA.Infrastructure/Data/ApplicationDbContext.cs b/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
index cfcea3b..b7d2a16 100644
--- a/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
+++ b/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
@@ -19,6 +19,7 @@ public class ApplicationDbContext : DbContext, IAppDbContext
     public DbSet<PlatformCredential> PlatformCredentials => Set<PlatformCredential>();
     public DbSet<BrandProfile> BrandProfiles => Set<BrandProfile>();
     public DbSet<BrandRankingProfile> BrandRankingProfiles => Set<BrandRankingProfile>();
+    public DbSet<ChannelMetricSnapshot> ChannelMetricSnapshots => Set<ChannelMetricSnapshot>();
 
     public void SetOriginalValue<TEntity>(TEntity entity, string propertyName, object value)
         where TEntity : class
diff --git a/src/PBA.Infrastructure/Data/Configurations/ChannelMetricSnapshotConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/ChannelMetricSnapshotConfiguration.cs
new file mode 100644
index 0000000..4c6b781
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Configurations/ChannelMetricSnapshotConfiguration.cs
@@ -0,0 +1,52 @@
+using System.Text.Json;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.ChangeTracking;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
+using PBA.Domain.Entities;
+
+namespace PBA.Infrastructure.Data.Configurations;
+
+public class ChannelMetricSnapshotConfiguration : IEntityTypeConfiguration<ChannelMetricSnapshot>
+{
+    public void Configure(EntityTypeBuilder<ChannelMetricSnapshot> builder)
+    {
+        builder.HasKey(s => s.Id);
+
+        builder.Property(s => s.Platform).HasConversion<int>();
+        builder.Property(s => s.Scope).HasConversion<int>();
+
+        // Npgsql maps DateOnly to `date` natively.
+        builder.Property(s => s.SnapshotDate).IsRequired();
+
+        builder.Property(s => s.VideoId).IsRequired().HasMaxLength(128);
+        builder.Property(s => s.VideoTitle).HasMaxLength(500);
+
+        // Metrics: a dictionary of a value type isn't mappable by the InMemory provider, so it goes through
+        // an explicit JSON string converter (stored as jsonb on Npgsql, as a string on InMemory) — same
+        // pattern as Idea.PillarSubScores. The converter is the sole read/write path, so default
+        // JsonSerializerOptions is self-consistent.
+        var metricsConverter = new ValueConverter<IReadOnlyDictionary<string, long>, string>(
+            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
+            v => JsonSerializer.Deserialize<Dictionary<string, long>>(v, (JsonSerializerOptions?)null)
+                 ?? new Dictionary<string, long>());
+        var metricsComparer = new ValueComparer<IReadOnlyDictionary<string, long>>(
+            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
+                      == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
+            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
+            v => JsonSerializer.Deserialize<Dictionary<string, long>>(
+                     JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!);
+        builder.Property(s => s.Metrics)
+            .HasColumnType("jsonb")
+            .IsRequired()
+            .HasConversion(metricsConverter, metricsComparer);
+
+        // Non-nullable VideoId => one Account row per platform-day (sentinel "") and one row per video-day.
+        // Enables a real ON CONFLICT upsert in the poller (section-05).
+        builder.HasIndex(s => new { s.Platform, s.SnapshotDate, s.Scope, s.VideoId })
+            .IsUnique();
+
+        // Range index for trend/range queries (section-06).
+        builder.HasIndex(s => new { s.Platform, s.SnapshotDate });
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs
index 0e1e06d..7a24a1b 100644
--- a/src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs
+++ b/src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs
@@ -16,9 +16,15 @@ public class PlatformCredentialConfiguration : IEntityTypeConfiguration<Platform
         builder.Property(c => c.EncryptedIntegrationToken).HasMaxLength(4000);
         builder.Property(c => c.Scopes).HasMaxLength(1000);
 
+        builder.Property(c => c.Purpose)
+            .HasConversion<int>();
+
         builder.HasIndex(c => new { c.Platform, c.IsActive });
 
-        builder.HasIndex(c => c.Platform)
+        // One active credential per (Platform, Purpose): a Publishing token and an Analytics token can
+        // coexist for the same platform, but never two active credentials of the same purpose. Replaces
+        // the old single-column unique index on Platform alone.
+        builder.HasIndex(c => new { c.Platform, c.Purpose })
             .IsUnique()
             .HasFilter("\"IsActive\" = true");
     }
diff --git a/src/PBA.Infrastructure/Data/Migrations/20260717191429_AddChannelAnalytics.Designer.cs b/src/PBA.Infrastructure/Data/Migrations/20260717191429_AddChannelAnalytics.Designer.cs
new file mode 100644
index 0000000..48d09e6
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Migrations/20260717191429_AddChannelAnalytics.Designer.cs
@@ -0,0 +1,856 @@
+﻿// <auto-generated />
+using System;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Infrastructure;
+using Microsoft.EntityFrameworkCore.Migrations;
+using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
+using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
+using PBA.Infrastructure.Data;
+using Pgvector;
+
+#nullable disable
+
+namespace PBA.Infrastructure.Data.Migrations
+{
+    [DbContext(typeof(ApplicationDbContext))]
+    [Migration("20260717191429_AddChannelAnalytics")]
+    partial class AddChannelAnalytics
+    {
+        /// <inheritdoc />
+        protected override void BuildTargetModel(ModelBuilder modelBuilder)
+        {
+#pragma warning disable 612, 618
+            modelBuilder
+                .HasAnnotation("ProductVersion", "10.0.7")
+                .HasAnnotation("Relational:MaxIdentifierLength", 63);
+
+            NpgsqlModelBuilderExtensions.HasPostgresExtension(modelBuilder, "vector");
+            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
+
+            modelBuilder.Entity("PBA.Domain.Entities.BrandPillar", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("BrandRankingProfileId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Description")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<Vector>("DescriptionEmbedding")
+                        .HasColumnType("vector(1536)");
+
+                    b.Property<string>("Name")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<int>("Order")
+                        .HasColumnType("integer");
+
+                    b.Property<double>("Weight")
+                        .HasColumnType("double precision");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("BrandRankingProfileId");
+
+                    b.ToTable("BrandPillars", (string)null);
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.BrandProfile", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.PrimitiveCollection<string>("AvoidWords")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("ExamplePosts")
+                        .HasColumnType("text");
+
+                    b.Property<string>("LearningLog")
+                        .HasColumnType("text");
+
+                    b.Property<string>("Personality")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<string>("Tone")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.PrimitiveCollection<string>("Topics")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.PrimitiveCollection<string>("Vocabulary")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.HasKey("Id");
+
+                    b.ToTable("BrandProfiles");
+
+                    b.HasData(
+                        new
+                        {
+                            Id = new Guid("00000000-0000-0000-0000-000000000001"),
+                            AvoidWords = "[]",
+                            Personality = "",
+                            Tone = "",
+                            Topics = "[]",
+                            UpdatedAt = new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
+                            Vocabulary = "[]"
+                        });
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.BrandRankingProfile", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<double>("AntiTopicMultiplier")
+                        .HasColumnType("double precision");
+
+                    b.PrimitiveCollection<string>("AntiTopics")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("AudiencePrimary")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<string>("AudienceSecondary")
+                        .HasColumnType("text");
+
+                    b.Property<double>("AuthorityBoost")
+                        .HasColumnType("double precision");
+
+                    b.PrimitiveCollection<string>("AuthorityTopics")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<double>("DecayFloor")
+                        .HasColumnType("double precision");
+
+                    b.Property<double>("HalfLifeDays")
+                        .HasColumnType("double precision");
+
+                    b.Property<bool>("IsActive")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("Positioning")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Version")
+                        .HasColumnType("integer");
+
+                    b.PrimitiveCollection<string>("VoiceMarkers")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<uint>("Xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IsActive")
+                        .IsUnique()
+                        .HasFilter("\"IsActive\" = true");
+
+                    b.ToTable("BrandRankingProfiles", (string)null);
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.ChannelMetricSnapshot", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CapturedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Metrics")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Scope")
+                        .HasColumnType("integer");
+
+                    b.Property<DateOnly>("SnapshotDate")
+                        .HasColumnType("date");
+
+                    b.Property<string>("VideoId")
+                        .IsRequired()
+                        .HasMaxLength(128)
+                        .HasColumnType("character varying(128)");
+
+                    b.Property<string>("VideoTitle")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Platform", "SnapshotDate");
+
+                    b.HasIndex("Platform", "SnapshotDate", "Scope", "VideoId")
+                        .IsUnique();
+
+                    b.ToTable("ChannelMetricSnapshots");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.Content", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Body")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<int>("ContentType")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("HangfireJobId")
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<bool>("IsDeleted")
+                        .HasColumnType("boolean");
+
+                    b.Property<Guid?>("ParentContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("PrimaryPlatform")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset?>("PublishedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset?>("ScheduledAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid?>("SourceIdeaId")
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.PrimitiveCollection<string>("Tags")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.PrimitiveCollection<string>("TargetPlatforms")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<decimal?>("ViralityPrediction")
+                        .HasPrecision(5, 2)
+                        .HasColumnType("numeric(5,2)");
+
+                    b.Property<decimal?>("VoiceScore")
+                        .HasPrecision(5, 2)
+                        .HasColumnType("numeric(5,2)");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ParentContentId");
+
+                    b.HasIndex("SourceIdeaId");
+
+                    b.ToTable("Contents");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.ContentPlatformPublish", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("Comments")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ErrorMessage")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<int>("Likes")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset?>("MetricsRefreshedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset?>("NextRetryAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("PlatformPostId")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<DateTimeOffset?>("PublishedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("PublishedUrl")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<int>("RetryCount")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("Shares")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Views")
+                        .HasColumnType("integer");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentId");
+
+                    b.HasIndex("Platform", "Status");
+
+                    b.ToTable("ContentPlatformPublishes");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.Digest", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateOnly>("Date")
+                        .HasColumnType("date");
+
+                    b.Property<string>("Intro")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<int>("ItemCount")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Kind")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(300)
+                        .HasColumnType("character varying(300)");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Date", "Kind")
+                        .IsUnique();
+
+                    b.ToTable("Digests");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.DigestItem", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("DigestId")
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("IdeaId")
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("Rank")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Score")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("WhyItMatters")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("DigestId");
+
+                    b.HasIndex("IdeaId");
+
+                    b.ToTable("DigestItems");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.FeedItem", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("ActionTargetId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ActionType")
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Data")
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset?>("ExpiresAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsActedOn")
+                        .HasColumnType("boolean");
+
+                    b.Property<bool>("IsRead")
+                        .HasColumnType("boolean");
+
+                    b.Property<int>("Priority")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("Summary")
+                        .IsRequired()
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<int>("Type")
+                        .HasColumnType("integer");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IsRead", "CreatedAt");
+
+                    b.HasIndex("Type", "CreatedAt");
+
+                    b.HasIndex("Type", "IsActedOn");
+
+                    b.ToTable("FeedItems");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.Idea", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("AIConnections")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset?>("AlertedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Category")
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<DateTimeOffset?>("ClusteredAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DeduplicationKey")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<string>("Description")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset>("DetectedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid?>("DuplicateOfId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset?>("EmbeddedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Vector>("Embedding")
+                        .HasColumnType("vector(1536)");
+
+                    b.Property<Guid?>("IdeaSourceId")
+                        .HasColumnType("uuid");
+
+                    b.Property<bool?>("IsAntiTopic")
+                        .HasColumnType("boolean");
+
+                    b.Property<bool?>("IsAuthorityTopic")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("PillarSubScores")
+                        .IsRequired()
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("jsonb")
+                        .HasDefaultValueSql("'[]'::jsonb");
+
+                    b.Property<int?>("Score")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("ScoreAttempts")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<string>("ScoreReason")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset?>("ScoredAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int?>("ScoredProfileVersion")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("SourceName")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("Summary")
+                        .HasColumnType("text");
+
+                    b.PrimitiveCollection<string>("Tags")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("ThumbnailUrl")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<string>("Url")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("AlertedAt");
+
+                    b.HasIndex("DeduplicationKey");
+
+                    b.HasIndex("DuplicateOfId");
+
+                    b.HasIndex("IdeaSourceId");
+
+                    b.HasIndex("Score");
+
+                    b.HasIndex("ScoredAt");
+
+                    b.HasIndex("ScoredProfileVersion");
+
+                    b.ToTable("Ideas");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.IdeaSource", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ApiUrl")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<string>("Category")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<int>("ConsecutiveFailures")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("FeedUrl")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<bool>("IsEnabled")
+                        .HasColumnType("boolean");
+
+                    b.Property<bool>("IsMicrosoftSource")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("LastError")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset?>("LastPolledAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset?>("LastSuccessAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Name")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<int>("PollIntervalMinutes")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Type")
+                        .HasColumnType("integer");
+
+                    b.HasKey("Id");
+
+                    b.ToTable("IdeaSources");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.PlatformCredential", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset?>("AccessTokenExpiresAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("EncryptedAccessToken")
+                        .IsRequired()
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<string>("EncryptedCookies")
+                        .HasMaxLength(8000)
+                        .HasColumnType("character varying(8000)");
+
+                    b.Property<string>("EncryptedIntegrationToken")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<string>("EncryptedRefreshToken")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<bool>("IsActive")
+                        .HasColumnType("boolean");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Purpose")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset?>("RefreshTokenExpiresAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Scopes")
+                        .HasMaxLength(1000)
+                        .HasColumnType("character varying(1000)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Platform", "IsActive");
+
+                    b.HasIndex("Platform", "Purpose")
+                        .IsUnique()
+                        .HasFilter("\"IsActive\" = true");
+
+                    b.ToTable("PlatformCredentials");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.SavedIdea", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("IdeaId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Notes")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset>("SavedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("SuggestedAngle")
+                        .HasColumnType("text");
+
+                    b.PrimitiveCollection<string>("SuggestedPlatforms")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.PrimitiveCollection<string>("Tags")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IdeaId")
+                        .IsUnique();
+
+                    b.ToTable("SavedIdeas");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.BrandPillar", b =>
+                {
+                    b.HasOne("PBA.Domain.Entities.BrandRankingProfile", null)
+                        .WithMany("Pillars")
+                        .HasForeignKey("BrandRankingProfileId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.Content", b =>
+                {
+                    b.HasOne("PBA.Domain.Entities.Content", "ParentContent")
+                        .WithMany("Children")
+                        .HasForeignKey("ParentContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+
+                    b.HasOne("PBA.Domain.Entities.Idea", "SourceIdea")
+                        .WithMany()
+                        .HasForeignKey("SourceIdeaId")
+                        .OnDelete(DeleteBehavior.SetNull);
+
+                    b.Navigation("ParentContent");
+
+                    b.Navigation("SourceIdea");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.ContentPlatformPublish", b =>
+                {
+                    b.HasOne("PBA.Domain.Entities.Content", "Content")
+                        .WithMany("CrossPosts")
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.Navigation("Content");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.DigestItem", b =>
+                {
+                    b.HasOne("PBA.Domain.Entities.Digest", "Digest")
+                        .WithMany("Items")
+                        .HasForeignKey("DigestId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.HasOne("PBA.Domain.Entities.Idea", "Idea")
+                        .WithMany()
+                        .HasForeignKey("IdeaId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.Navigation("Digest");
+
+                    b.Navigation("Idea");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.Idea", b =>
+                {
+                    b.HasOne("PBA.Domain.Entities.Idea", null)
+                        .WithMany()
+                        .HasForeignKey("DuplicateOfId")
+                        .OnDelete(DeleteBehavior.SetNull);
+
+                    b.HasOne("PBA.Domain.Entities.IdeaSource", "IdeaSource")
+                        .WithMany("Ideas")
+                        .HasForeignKey("IdeaSourceId")
+                        .OnDelete(DeleteBehavior.SetNull);
+
+                    b.Navigation("IdeaSource");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.SavedIdea", b =>
+                {
+                    b.HasOne("PBA.Domain.Entities.Idea", "Idea")
+                        .WithOne("SavedDetails")
+                        .HasForeignKey("PBA.Domain.Entities.SavedIdea", "IdeaId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.Navigation("Idea");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.BrandRankingProfile", b =>
+                {
+                    b.Navigation("Pillars");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.Content", b =>
+                {
+                    b.Navigation("Children");
+
+                    b.Navigation("CrossPosts");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.Digest", b =>
+                {
+                    b.Navigation("Items");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.Idea", b =>
+                {
+                    b.Navigation("SavedDetails");
+                });
+
+            modelBuilder.Entity("PBA.Domain.Entities.IdeaSource", b =>
+                {
+                    b.Navigation("Ideas");
+                });
+#pragma warning restore 612, 618
+        }
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Migrations/20260717191429_AddChannelAnalytics.cs b/src/PBA.Infrastructure/Data/Migrations/20260717191429_AddChannelAnalytics.cs
new file mode 100644
index 0000000..1c1a5d8
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Migrations/20260717191429_AddChannelAnalytics.cs
@@ -0,0 +1,84 @@
+﻿using System;
+using Microsoft.EntityFrameworkCore.Migrations;
+
+#nullable disable
+
+namespace PBA.Infrastructure.Data.Migrations
+{
+    /// <inheritdoc />
+    public partial class AddChannelAnalytics : Migration
+    {
+        /// <inheritdoc />
+        protected override void Up(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.DropIndex(
+                name: "IX_PlatformCredentials_Platform",
+                table: "PlatformCredentials");
+
+            migrationBuilder.AddColumn<int>(
+                name: "Purpose",
+                table: "PlatformCredentials",
+                type: "integer",
+                nullable: false,
+                defaultValue: 0);
+
+            migrationBuilder.CreateTable(
+                name: "ChannelMetricSnapshots",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Platform = table.Column<int>(type: "integer", nullable: false),
+                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
+                    Scope = table.Column<int>(type: "integer", nullable: false),
+                    VideoId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
+                    VideoTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
+                    Metrics = table.Column<string>(type: "jsonb", nullable: false),
+                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_ChannelMetricSnapshots", x => x.Id);
+                });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_PlatformCredentials_Platform_Purpose",
+                table: "PlatformCredentials",
+                columns: new[] { "Platform", "Purpose" },
+                unique: true,
+                filter: "\"IsActive\" = true");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_ChannelMetricSnapshots_Platform_SnapshotDate",
+                table: "ChannelMetricSnapshots",
+                columns: new[] { "Platform", "SnapshotDate" });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_ChannelMetricSnapshots_Platform_SnapshotDate_Scope_VideoId",
+                table: "ChannelMetricSnapshots",
+                columns: new[] { "Platform", "SnapshotDate", "Scope", "VideoId" },
+                unique: true);
+        }
+
+        /// <inheritdoc />
+        protected override void Down(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.DropTable(
+                name: "ChannelMetricSnapshots");
+
+            migrationBuilder.DropIndex(
+                name: "IX_PlatformCredentials_Platform_Purpose",
+                table: "PlatformCredentials");
+
+            migrationBuilder.DropColumn(
+                name: "Purpose",
+                table: "PlatformCredentials");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_PlatformCredentials_Platform",
+                table: "PlatformCredentials",
+                column: "Platform",
+                unique: true,
+                filter: "\"IsActive\" = true");
+        }
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs b/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
index 99f9f74..58e85b6 100644
--- a/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
+++ b/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
@@ -176,6 +176,47 @@ namespace PBA.Infrastructure.Data.Migrations
                     b.ToTable("BrandRankingProfiles", (string)null);
                 });
 
+            modelBuilder.Entity("PBA.Domain.Entities.ChannelMetricSnapshot", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CapturedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Metrics")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Scope")
+                        .HasColumnType("integer");
+
+                    b.Property<DateOnly>("SnapshotDate")
+                        .HasColumnType("date");
+
+                    b.Property<string>("VideoId")
+                        .IsRequired()
+                        .HasMaxLength(128)
+                        .HasColumnType("character varying(128)");
+
+                    b.Property<string>("VideoTitle")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Platform", "SnapshotDate");
+
+                    b.HasIndex("Platform", "SnapshotDate", "Scope", "VideoId")
+                        .IsUnique();
+
+                    b.ToTable("ChannelMetricSnapshots");
+                });
+
             modelBuilder.Entity("PBA.Domain.Entities.Content", b =>
                 {
                     b.Property<Guid>("Id")
@@ -640,6 +681,9 @@ namespace PBA.Infrastructure.Data.Migrations
                     b.Property<int>("Platform")
                         .HasColumnType("integer");
 
+                    b.Property<int>("Purpose")
+                        .HasColumnType("integer");
+
                     b.Property<DateTimeOffset?>("RefreshTokenExpiresAt")
                         .HasColumnType("timestamp with time zone");
 
@@ -652,12 +696,12 @@ namespace PBA.Infrastructure.Data.Migrations
 
                     b.HasKey("Id");
 
-                    b.HasIndex("Platform")
+                    b.HasIndex("Platform", "IsActive");
+
+                    b.HasIndex("Platform", "Purpose")
                         .IsUnique()
                         .HasFilter("\"IsActive\" = true");
 
-                    b.HasIndex("Platform", "IsActive");
-
                     b.ToTable("PlatformCredentials");
                 });
 
diff --git a/tests/PBA.Infrastructure.Tests/Data/ChannelAnalyticsModelTests.cs b/tests/PBA.Infrastructure.Tests/Data/ChannelAnalyticsModelTests.cs
new file mode 100644
index 0000000..b83f90c
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Data/ChannelAnalyticsModelTests.cs
@@ -0,0 +1,79 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Data;
+
+// Provider-agnostic model + config tests (run on the InMemory provider; the jsonb converter makes the
+// metric-bag round-trip work there). Index-enforcement lives in ChannelAnalyticsPersistenceTests (real DB).
+public class ChannelAnalyticsModelTests
+{
+    [Fact]
+    public void Platform_Enum_YouTubeInstagramTikTok_HaveStableNumericValues()
+    {
+        // Persisted PlatformCredential.Platform values depend on these numbers — append only, never renumber.
+        Assert.Equal(0, (int)Platform.Blog);
+        Assert.Equal(1, (int)Platform.Substack);
+        Assert.Equal(2, (int)Platform.LinkedIn);
+        Assert.Equal(3, (int)Platform.Twitter);
+        Assert.Equal(4, (int)Platform.Reddit);
+        Assert.Equal(5, (int)Platform.YouTube);
+        Assert.Equal(6, (int)Platform.Medium);
+        Assert.Equal(7, (int)Platform.Instagram);
+        Assert.Equal(8, (int)Platform.TikTok);
+    }
+
+    [Fact]
+    public void ChannelMetricSnapshot_AccountScope_UsesEmptyStringVideoIdSentinel()
+    {
+        var snapshot = new ChannelMetricSnapshot
+        {
+            Platform = Platform.YouTube,
+            Scope = SnapshotScope.Account
+        };
+
+        // Sentinel "" (never null) so the unique index + ON CONFLICT upsert function in PostgreSQL.
+        Assert.NotNull(snapshot.VideoId);
+        Assert.Equal(string.Empty, snapshot.VideoId);
+    }
+
+    [Fact]
+    public void PlatformCredential_DefaultsPurposeToPublishing()
+    {
+        var credential = new PlatformCredential { Platform = Platform.LinkedIn };
+        Assert.Equal(CredentialPurpose.Publishing, credential.Purpose);
+    }
+
+    [Fact]
+    public async Task ChannelMetricSnapshotConfiguration_MetricsBag_RoundTripsThroughJsonb()
+    {
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString())
+            .Options;
+        var id = Guid.NewGuid();
+
+        await using (var db = new ApplicationDbContext(options))
+        {
+            db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
+            {
+                Id = id,
+                Platform = Platform.YouTube,
+                SnapshotDate = new DateOnly(2026, 7, 17),
+                Scope = SnapshotScope.Account,
+                Metrics = new Dictionary<string, long> { ["subscribers"] = 1234, ["views"] = 99 }
+            });
+            await db.SaveChangesAsync();
+        }
+
+        // Fresh context forces a real deserialize through the ValueConverter (not a change-tracker cache hit).
+        await using (var db = new ApplicationDbContext(options))
+        {
+            var reloaded = await db.ChannelMetricSnapshots.SingleAsync(s => s.Id == id);
+            Assert.Equal(2, reloaded.Metrics.Count);
+            Assert.Equal(1234L, reloaded.Metrics["subscribers"]);
+            Assert.Equal(99L, reloaded.Metrics["views"]);
+        }
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Data/ChannelAnalyticsPersistenceTests.cs b/tests/PBA.Infrastructure.Tests/Data/ChannelAnalyticsPersistenceTests.cs
new file mode 100644
index 0000000..4778b46
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Data/ChannelAnalyticsPersistenceTests.cs
@@ -0,0 +1,221 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Infrastructure;
+using Microsoft.EntityFrameworkCore.Migrations;
+using Npgsql;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Testcontainers.PostgreSql;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Data;
+
+// Real-DB enforcement: the composite filtered unique index on PlatformCredentials and the snapshot unique
+// index only mean anything on Postgres (the InMemory provider ignores unique indexes). Mirrors the
+// Testcontainers pattern in CutoverTests. Requires Docker (pgvector/pgvector:pg16).
+public sealed class ChannelAnalyticsDbFixture : IAsyncLifetime
+{
+    private PostgreSqlContainer _container = null!;
+    public NpgsqlDataSource DataSource { get; private set; } = null!;
+    public DbContextOptions<ApplicationDbContext> Options { get; private set; } = null!;
+
+    public async Task InitializeAsync()
+    {
+        _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
+        await _container.StartAsync();
+
+        var builder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
+        builder.EnableDynamicJson();
+        builder.UseVector();
+        DataSource = builder.Build();
+
+        Options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseNpgsql(DataSource, o => o.UseVector())
+            .Options;
+
+        await using var db = new ApplicationDbContext(Options);
+        await db.Database.MigrateAsync();
+    }
+
+    public ApplicationDbContext CreateContext() => new(Options);
+
+    public async Task DisposeAsync()
+    {
+        await DataSource.DisposeAsync();
+        await _container.DisposeAsync();
+    }
+}
+
+public class ChannelAnalyticsPersistenceTests(ChannelAnalyticsDbFixture fixture)
+    : IClassFixture<ChannelAnalyticsDbFixture>
+{
+    private static PlatformCredential Credential(Platform platform, CredentialPurpose purpose) => new()
+    {
+        Platform = platform,
+        Purpose = purpose,
+        EncryptedAccessToken = "token",
+        IsActive = true
+    };
+
+    [Fact]
+    public async Task PlatformCredentialConfiguration_AllowsActivePublishingAndAnalyticsForSamePlatform()
+    {
+        await using var db = fixture.CreateContext();
+
+        db.PlatformCredentials.Add(Credential(Platform.Instagram, CredentialPurpose.Publishing));
+        db.PlatformCredentials.Add(Credential(Platform.Instagram, CredentialPurpose.Analytics));
+
+        // Both persist: the unique filter is on (Platform, Purpose), not Platform alone.
+        await db.SaveChangesAsync();
+
+        var count = await db.PlatformCredentials
+            .CountAsync(c => c.Platform == Platform.Instagram && c.IsActive);
+        Assert.Equal(2, count);
+    }
+
+    [Fact]
+    public async Task PlatformCredentialConfiguration_RejectsTwoActiveAnalyticsCredentials_SamePlatform()
+    {
+        await using var db = fixture.CreateContext();
+
+        db.PlatformCredentials.Add(Credential(Platform.TikTok, CredentialPurpose.Analytics));
+        await db.SaveChangesAsync();
+
+        db.PlatformCredentials.Add(Credential(Platform.TikTok, CredentialPurpose.Analytics));
+        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
+    }
+
+    [Fact]
+    public async Task ChannelMetricSnapshotConfiguration_UniqueIndex_RejectsDuplicateAccountRow_SamePlatformDate()
+    {
+        await using var db = fixture.CreateContext();
+        var date = new DateOnly(2026, 1, 5);
+
+        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
+        {
+            Platform = Platform.YouTube, SnapshotDate = date, Scope = SnapshotScope.Account
+        });
+        await db.SaveChangesAsync();
+
+        // Same (Platform, Date, Scope=Account, VideoId="") -> unique violation.
+        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
+        {
+            Platform = Platform.YouTube, SnapshotDate = date, Scope = SnapshotScope.Account
+        });
+        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
+    }
+
+    [Fact]
+    public async Task ChannelMetricSnapshotConfiguration_UniqueIndex_RejectsDuplicateVideoRow_SamePlatformDateVideo()
+    {
+        await using var db = fixture.CreateContext();
+        var date = new DateOnly(2026, 1, 6);
+
+        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
+        {
+            Platform = Platform.YouTube, SnapshotDate = date, Scope = SnapshotScope.Video, VideoId = "abc"
+        });
+        await db.SaveChangesAsync();
+
+        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
+        {
+            Platform = Platform.YouTube, SnapshotDate = date, Scope = SnapshotScope.Video, VideoId = "abc"
+        });
+        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
+    }
+
+    [Fact]
+    public async Task ChannelMetricSnapshotConfiguration_MetricsBag_RoundTripsThroughRealJsonb()
+    {
+        var id = Guid.NewGuid();
+        await using (var db = fixture.CreateContext())
+        {
+            db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
+            {
+                Id = id,
+                Platform = Platform.TikTok,
+                SnapshotDate = new DateOnly(2026, 1, 7),
+                Scope = SnapshotScope.Account,
+                Metrics = new Dictionary<string, long> { ["followers"] = 5000, ["likes"] = 120345 }
+            });
+            await db.SaveChangesAsync();
+        }
+
+        await using (var db = fixture.CreateContext())
+        {
+            var reloaded = await db.ChannelMetricSnapshots.SingleAsync(s => s.Id == id);
+            Assert.Equal(5000L, reloaded.Metrics["followers"]);
+            Assert.Equal(120345L, reloaded.Metrics["likes"]);
+        }
+    }
+}
+
+// Own container: this test mutates schema (migrate up then down), so it cannot share the class fixture.
+public class ChannelAnalyticsMigrationTests
+{
+    private const string ThisMigration = "20260717191429_AddChannelAnalytics";
+    private const string PreviousMigration = "20260617144341_AddIsMicrosoftSource";
+
+    [Fact]
+    public async Task Migration_AddChannelAnalytics_AppliesAndReverts_OnCleanDb()
+    {
+        await using var container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
+        await container.StartAsync();
+
+        var builder = new NpgsqlDataSourceBuilder(container.GetConnectionString());
+        builder.EnableDynamicJson();
+        builder.UseVector();
+        await using var dataSource = builder.Build();
+
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseNpgsql(dataSource, o => o.UseVector())
+            .Options;
+
+        // Apply everything up to and including AddChannelAnalytics.
+        await using (var db = new ApplicationDbContext(options))
+        {
+            await db.Database.MigrateAsync();
+        }
+
+        await using (var conn = dataSource.CreateConnection())
+        {
+            await conn.OpenAsync();
+            Assert.True(await ScalarBoolAsync(conn,
+                "SELECT to_regclass('public.\"ChannelMetricSnapshots\"') IS NOT NULL"), "snapshots table");
+            Assert.True(await ScalarBoolAsync(conn,
+                "SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='PlatformCredentials' AND column_name='Purpose')"),
+                "Purpose column");
+            Assert.True(await ScalarBoolAsync(conn,
+                "SELECT to_regclass('public.\"IX_PlatformCredentials_Platform_Purpose\"') IS NOT NULL"),
+                "new composite index");
+            Assert.False(await ScalarBoolAsync(conn,
+                "SELECT to_regclass('public.\"IX_PlatformCredentials_Platform\"') IS NOT NULL"),
+                "old single-column index dropped");
+        }
+
+        // Revert AddChannelAnalytics -> the previous migration.
+        await using (var db = new ApplicationDbContext(options))
+        {
+            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
+        }
+
+        await using (var conn = dataSource.CreateConnection())
+        {
+            await conn.OpenAsync();
+            Assert.False(await ScalarBoolAsync(conn,
+                "SELECT to_regclass('public.\"ChannelMetricSnapshots\"') IS NOT NULL"), "snapshots table gone");
+            Assert.False(await ScalarBoolAsync(conn,
+                "SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='PlatformCredentials' AND column_name='Purpose')"),
+                "Purpose column gone");
+            Assert.True(await ScalarBoolAsync(conn,
+                "SELECT to_regclass('public.\"IX_PlatformCredentials_Platform\"') IS NOT NULL"),
+                "old single-column index restored");
+        }
+    }
+
+    private static async Task<bool> ScalarBoolAsync(NpgsqlConnection conn, string sql)
+    {
+        await using var cmd = new NpgsqlCommand(sql, conn);
+        return (bool)(await cmd.ExecuteScalarAsync())!;
+    }
+}
