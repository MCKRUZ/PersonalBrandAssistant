diff --git a/src/PBA.Api/Program.cs b/src/PBA.Api/Program.cs
index af86ba6..73522ec 100644
--- a/src/PBA.Api/Program.cs
+++ b/src/PBA.Api/Program.cs
@@ -72,6 +72,16 @@ if (app.Environment.IsDevelopment())
     });
 }
 
+// Seeds the v1 active BrandRankingProfile (required reference data the ranker depends on). Exposed
+// outside the dev guard so the cutover (section-12) can invoke it in production after migrations.
+// Idempotent + race-safe: no-ops once an active profile exists.
+app.MapPost("/api/brand-ranking-profile/seed",
+    async (IBrandRankingProfileSeedService seedService, CancellationToken ct) =>
+    {
+        var count = await seedService.SeedAsync(ct);
+        return Results.Ok(new { seeded = count });
+    });
+
 app.Run();
 
 public partial class Program { }
diff --git a/src/PBA.Application/Common/Interfaces/IAppDbContext.cs b/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
index 1e3aae2..ba8f00c 100644
--- a/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
+++ b/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
@@ -9,6 +9,7 @@ public interface IAppDbContext
     DbSet<ContentPlatformPublish> ContentPlatformPublishes { get; }
     DbSet<PlatformCredential> PlatformCredentials { get; }
     DbSet<BrandProfile> BrandProfiles { get; }
+    DbSet<BrandRankingProfile> BrandRankingProfiles { get; }
     DbSet<Idea> Ideas { get; }
     DbSet<SavedIdea> SavedIdeas { get; }
     DbSet<IdeaSource> IdeaSources { get; }
diff --git a/src/PBA.Application/Common/Interfaces/IBrandRankingProfileSeedService.cs b/src/PBA.Application/Common/Interfaces/IBrandRankingProfileSeedService.cs
new file mode 100644
index 0000000..882caf9
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/IBrandRankingProfileSeedService.cs
@@ -0,0 +1,11 @@
+namespace PBA.Application.Common.Interfaces;
+
+/// <summary>
+/// Seeds the v1 active <c>BrandRankingProfile</c>. Idempotent and race-safe across the two deploy
+/// hosts: returns 0 if an active profile already exists, and tolerates a concurrent insert losing the
+/// partial-unique-index race (R-L5).
+/// </summary>
+public interface IBrandRankingProfileSeedService
+{
+    Task<int> SeedAsync(CancellationToken cancellationToken = default);
+}
diff --git a/src/PBA.Domain/Entities/BrandPillar.cs b/src/PBA.Domain/Entities/BrandPillar.cs
new file mode 100644
index 0000000..0d3957e
--- /dev/null
+++ b/src/PBA.Domain/Entities/BrandPillar.cs
@@ -0,0 +1,19 @@
+namespace PBA.Domain.Entities;
+
+/// <summary>
+/// A weighted brand pillar the feed ranker scores ideas against. Identity is <see cref="Id"/>
+/// (sub-scores are keyed by it, R-C3); the name is display-only and may change without breaking
+/// stored sub-scores. The description is what both the LLM analyzer and the embedding pre-filter
+/// score against. <see cref="DescriptionEmbedding"/> stores the pillar vector; it maps to a
+/// vector(1536) column via an EF value converter (Domain stays free of the Npgsql/pgvector deps).
+/// </summary>
+public class BrandPillar
+{
+    public Guid Id { get; init; } = Guid.NewGuid();
+    public Guid BrandRankingProfileId { get; init; }
+    public required string Name { get; set; }
+    public required string Description { get; set; }
+    public double Weight { get; set; }              // 0..1; weights across pillars sum ~1.0 (query-time)
+    public int Order { get; set; }
+    public float[]? DescriptionEmbedding { get; set; } // vector(1536); populated by the embedding service
+}
diff --git a/src/PBA.Domain/Entities/BrandRankingProfile.cs b/src/PBA.Domain/Entities/BrandRankingProfile.cs
new file mode 100644
index 0000000..84eb392
--- /dev/null
+++ b/src/PBA.Domain/Entities/BrandRankingProfile.cs
@@ -0,0 +1,66 @@
+namespace PBA.Domain.Entities;
+
+/// <summary>
+/// The editable source of truth the feed ranker scores ideas against. Exactly one row is active at a
+/// time (enforced by a partial unique index). <see cref="Version"/> bumps only when a DEFINITION field
+/// changes (anything the LLM analyzer prompt or pillar embeddings see) — that invalidates stored
+/// sub-scores and triggers a re-score sweep. Query-time knobs (weights, half-life, floor, multipliers)
+/// change without a bump, so re-weighting re-ranks instantly with zero LLM calls.
+/// </summary>
+public class BrandRankingProfile
+{
+    public Guid Id { get; init; } = Guid.NewGuid();
+    public int Version { get; set; }
+    public bool IsActive { get; set; }
+
+    // Definition fields (changing any of these bumps Version) ---------------------------------
+    public required string Positioning { get; set; }
+    public required string AudiencePrimary { get; set; }
+    public string? AudienceSecondary { get; set; }
+    public List<string> AuthorityTopics { get; set; } = [];
+    public List<string> AntiTopics { get; set; } = [];
+    public List<string> VoiceMarkers { get; set; } = [];
+    public List<BrandPillar> Pillars { get; set; } = [];
+
+    // Query-time ranking knobs (changing these does NOT bump Version) --------------------------
+    public double HalfLifeDays { get; set; } = 7;
+    public double DecayFloor { get; set; } = 0.075;
+    public double AntiTopicMultiplier { get; set; } = 0.1;
+    public double AuthorityBoost { get; set; } = 1.2;
+
+    /// <summary>Npgsql system-column (xmin) optimistic-concurrency token; mapped in EF config.</summary>
+    public uint Xmin { get; set; }
+    public DateTimeOffset UpdatedAt { get; set; }
+
+    /// <summary>
+    /// True if applying <paramref name="proposed"/> would change a definition field (positioning,
+    /// audience, topics, voice markers, or any pillar name/description, or adding/removing a pillar) —
+    /// which must bump <see cref="Version"/> and trigger a re-score. Pure; unit-tested in isolation.
+    /// Query-time knobs (weights, half-life, floor, multipliers) and pillar Order/Weight do NOT count.
+    /// </summary>
+    public bool RequiresVersionBump(BrandRankingProfile proposed)
+    {
+        if (Positioning != proposed.Positioning) return true;
+        if (AudiencePrimary != proposed.AudiencePrimary) return true;
+        if (AudienceSecondary != proposed.AudienceSecondary) return true;
+        if (!SequenceEqualUnordered(AuthorityTopics, proposed.AuthorityTopics)) return true;
+        if (!SequenceEqualUnordered(AntiTopics, proposed.AntiTopics)) return true;
+        if (!SequenceEqualUnordered(VoiceMarkers, proposed.VoiceMarkers)) return true;
+
+        // Pillars: bump on add/remove (by Id) or on any matched pillar's Name/Description change.
+        var current = Pillars.ToDictionary(p => p.Id);
+        var next = proposed.Pillars.ToDictionary(p => p.Id);
+        if (current.Count != next.Count) return true;
+        foreach (var (id, currentPillar) in current)
+        {
+            if (!next.TryGetValue(id, out var proposedPillar)) return true; // removed/replaced id
+            if (currentPillar.Name != proposedPillar.Name) return true;
+            if (currentPillar.Description != proposedPillar.Description) return true;
+        }
+
+        return false;
+    }
+
+    private static bool SequenceEqualUnordered(IEnumerable<string> a, IEnumerable<string> b)
+        => new HashSet<string>(a).SetEquals(b);
+}
diff --git a/src/PBA.Infrastructure/Data/ApplicationDbContext.cs b/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
index b89e307..4938560 100644
--- a/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
+++ b/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
@@ -18,11 +18,19 @@ public class ApplicationDbContext : DbContext, IAppDbContext
     public DbSet<DigestItem> DigestItems => Set<DigestItem>();
     public DbSet<PlatformCredential> PlatformCredentials => Set<PlatformCredential>();
     public DbSet<BrandProfile> BrandProfiles => Set<BrandProfile>();
+    public DbSet<BrandRankingProfile> BrandRankingProfiles => Set<BrandRankingProfile>();
 
     protected override void OnModelCreating(ModelBuilder modelBuilder)
     {
-        modelBuilder.HasPostgresExtension("vector"); // pgvector — required by vector(1536) columns
         modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
+
+        // pgvector mappings (vector extension + vector(1536) columns) only under the Npgsql provider;
+        // the InMemory test provider can't map the Vector provider type.
+        if (Database.IsNpgsql())
+        {
+            PgVectorModelConfiguration.Apply(modelBuilder);
+        }
+
         base.OnModelCreating(modelBuilder);
     }
 }
diff --git a/src/PBA.Infrastructure/Data/Configurations/BrandPillarConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/BrandPillarConfiguration.cs
new file mode 100644
index 0000000..95a32bc
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Configurations/BrandPillarConfiguration.cs
@@ -0,0 +1,21 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PBA.Domain.Entities;
+
+namespace PBA.Infrastructure.Data.Configurations;
+
+public class BrandPillarConfiguration : IEntityTypeConfiguration<BrandPillar>
+{
+    public void Configure(EntityTypeBuilder<BrandPillar> builder)
+    {
+        builder.ToTable("BrandPillars");
+        builder.HasKey(p => p.Id);
+
+        builder.Property(p => p.Name).HasMaxLength(200);
+        builder.Property(p => p.Description).HasColumnType("text");
+
+        // DescriptionEmbedding maps to vector(1536) via a float[]<->Vector converter, applied only
+        // under the Npgsql provider (PgVectorModelConfiguration) — the InMemory test provider can't
+        // map the Vector provider type, and stores the float[] natively instead.
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Configurations/BrandRankingProfileConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/BrandRankingProfileConfiguration.cs
new file mode 100644
index 0000000..f1c8394
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Configurations/BrandRankingProfileConfiguration.cs
@@ -0,0 +1,42 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PBA.Domain.Entities;
+
+namespace PBA.Infrastructure.Data.Configurations;
+
+public class BrandRankingProfileConfiguration : IEntityTypeConfiguration<BrandRankingProfile>
+{
+    public void Configure(EntityTypeBuilder<BrandRankingProfile> builder)
+    {
+        builder.ToTable("BrandRankingProfiles");
+        builder.HasKey(p => p.Id);
+
+        builder.Property(p => p.Positioning).HasColumnType("text");
+        builder.Property(p => p.AudiencePrimary).HasColumnType("text");
+        builder.Property(p => p.AudienceSecondary).HasColumnType("text");
+        builder.Property(p => p.AuthorityTopics).HasColumnType("jsonb");
+        builder.Property(p => p.AntiTopics).HasColumnType("jsonb");
+        builder.Property(p => p.VoiceMarkers).HasColumnType("jsonb");
+
+        builder.HasMany(p => p.Pillars)
+            .WithOne()
+            .HasForeignKey(pl => pl.BrandRankingProfileId)
+            .OnDelete(DeleteBehavior.Cascade);
+
+        // Optimistic concurrency via Npgsql's xmin system column (R-H3). xmin already exists on every
+        // table, so it is mapped, not created — Npgsql recognizes the "xmin"/"xid" mapping as the
+        // system column and emits no DDL column for it.
+        builder.Property(p => p.Xmin)
+            .HasColumnName("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        // Exactly one active profile (R-L4): partial unique index over IsActive WHERE IsActive = true.
+        builder.HasIndex(p => p.IsActive)
+            .IsUnique()
+            .HasFilter("\"IsActive\" = true");
+
+        // No HasData seed — seeding is an idempotent, race-safe runtime service (R-L5), not a data migration.
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Migrations/20260616135102_AddBrandRankingProfile.Designer.cs b/src/PBA.Infrastructure/Data/Migrations/20260616135102_AddBrandRankingProfile.Designer.cs
new file mode 100644
index 0000000..3d37903
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Migrations/20260616135102_AddBrandRankingProfile.Designer.cs
@@ -0,0 +1,778 @@
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
+    [Migration("20260616135102_AddBrandRankingProfile")]
+    partial class AddBrandRankingProfile
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
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(300)
+                        .HasColumnType("character varying(300)");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Date")
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
+                    b.Property<Guid?>("IdeaSourceId")
+                        .HasColumnType("uuid");
+
+                    b.Property<int?>("Score")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("ScoreReason")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset?>("ScoredAt")
+                        .HasColumnType("timestamp with time zone");
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
+                    b.HasIndex("Platform")
+                        .IsUnique()
+                        .HasFilter("\"IsActive\" = true");
+
+                    b.HasIndex("Platform", "IsActive");
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
diff --git a/src/PBA.Infrastructure/Data/Migrations/20260616135102_AddBrandRankingProfile.cs b/src/PBA.Infrastructure/Data/Migrations/20260616135102_AddBrandRankingProfile.cs
new file mode 100644
index 0000000..f7cb958
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Migrations/20260616135102_AddBrandRankingProfile.cs
@@ -0,0 +1,86 @@
+﻿using System;
+using Microsoft.EntityFrameworkCore.Migrations;
+using Pgvector;
+
+#nullable disable
+
+namespace PBA.Infrastructure.Data.Migrations
+{
+    /// <inheritdoc />
+    public partial class AddBrandRankingProfile : Migration
+    {
+        /// <inheritdoc />
+        protected override void Up(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.CreateTable(
+                name: "BrandRankingProfiles",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Version = table.Column<int>(type: "integer", nullable: false),
+                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
+                    Positioning = table.Column<string>(type: "text", nullable: false),
+                    AudiencePrimary = table.Column<string>(type: "text", nullable: false),
+                    AudienceSecondary = table.Column<string>(type: "text", nullable: true),
+                    AuthorityTopics = table.Column<string>(type: "jsonb", nullable: false),
+                    AntiTopics = table.Column<string>(type: "jsonb", nullable: false),
+                    VoiceMarkers = table.Column<string>(type: "jsonb", nullable: false),
+                    HalfLifeDays = table.Column<double>(type: "double precision", nullable: false),
+                    DecayFloor = table.Column<double>(type: "double precision", nullable: false),
+                    AntiTopicMultiplier = table.Column<double>(type: "double precision", nullable: false),
+                    AuthorityBoost = table.Column<double>(type: "double precision", nullable: false),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_BrandRankingProfiles", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "BrandPillars",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    BrandRankingProfileId = table.Column<Guid>(type: "uuid", nullable: false),
+                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
+                    Description = table.Column<string>(type: "text", nullable: false),
+                    Weight = table.Column<double>(type: "double precision", nullable: false),
+                    Order = table.Column<int>(type: "integer", nullable: false),
+                    DescriptionEmbedding = table.Column<Vector>(type: "vector(1536)", nullable: true)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_BrandPillars", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_BrandPillars_BrandRankingProfiles_BrandRankingProfileId",
+                        column: x => x.BrandRankingProfileId,
+                        principalTable: "BrandRankingProfiles",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.Cascade);
+                });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_BrandPillars_BrandRankingProfileId",
+                table: "BrandPillars",
+                column: "BrandRankingProfileId");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_BrandRankingProfiles_IsActive",
+                table: "BrandRankingProfiles",
+                column: "IsActive",
+                unique: true,
+                filter: "\"IsActive\" = true");
+        }
+
+        /// <inheritdoc />
+        protected override void Down(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.DropTable(
+                name: "BrandPillars");
+
+            migrationBuilder.DropTable(
+                name: "BrandRankingProfiles");
+        }
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs b/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
index a67b6cd..9e9154b 100644
--- a/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
+++ b/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
@@ -5,6 +5,7 @@ using Microsoft.EntityFrameworkCore.Infrastructure;
 using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
 using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
 using PBA.Infrastructure.Data;
+using Pgvector;
 
 #nullable disable
 
@@ -23,6 +24,40 @@ namespace PBA.Infrastructure.Data.Migrations
             NpgsqlModelBuilderExtensions.HasPostgresExtension(modelBuilder, "vector");
             NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
 
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
             modelBuilder.Entity("PBA.Domain.Entities.BrandProfile", b =>
                 {
                     b.Property<Guid>("Id")
@@ -76,6 +111,71 @@ namespace PBA.Infrastructure.Data.Migrations
                         });
                 });
 
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
             modelBuilder.Entity("PBA.Domain.Entities.Content", b =>
                 {
                     b.Property<Guid>("Id")
@@ -561,6 +661,15 @@ namespace PBA.Infrastructure.Data.Migrations
                     b.ToTable("SavedIdeas");
                 });
 
+            modelBuilder.Entity("PBA.Domain.Entities.BrandPillar", b =>
+                {
+                    b.HasOne("PBA.Domain.Entities.BrandRankingProfile", null)
+                        .WithMany("Pillars")
+                        .HasForeignKey("BrandRankingProfileId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
             modelBuilder.Entity("PBA.Domain.Entities.Content", b =>
                 {
                     b.HasOne("PBA.Domain.Entities.Content", "ParentContent")
@@ -634,6 +743,11 @@ namespace PBA.Infrastructure.Data.Migrations
                     b.Navigation("Idea");
                 });
 
+            modelBuilder.Entity("PBA.Domain.Entities.BrandRankingProfile", b =>
+                {
+                    b.Navigation("Pillars");
+                });
+
             modelBuilder.Entity("PBA.Domain.Entities.Content", b =>
                 {
                     b.Navigation("Children");
diff --git a/src/PBA.Infrastructure/Data/PgVectorModelConfiguration.cs b/src/PBA.Infrastructure/Data/PgVectorModelConfiguration.cs
new file mode 100644
index 0000000..5aac187
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/PgVectorModelConfiguration.cs
@@ -0,0 +1,30 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Domain.Entities;
+using Pgvector;
+
+namespace PBA.Infrastructure.Data;
+
+/// <summary>
+/// pgvector-specific mappings applied only under the Npgsql provider. Entities expose embeddings as
+/// <c>float[]?</c> (so the Domain stays free of the Npgsql/pgvector dependency); here they map to a
+/// <c>vector(1536)</c> column via a float[]&lt;-&gt;Vector value converter. The InMemory test provider
+/// can't map the <c>Vector</c> provider type, so this is skipped there and the float[] is stored natively.
+/// No pgvector SQL is ever issued over these columns (all cosine math runs in memory), so a value
+/// converter is safe.
+/// </summary>
+internal static class PgVectorModelConfiguration
+{
+    public const int Dimensions = 1536;
+
+    public static void Apply(ModelBuilder modelBuilder)
+    {
+        modelBuilder.HasPostgresExtension("vector");
+
+        modelBuilder.Entity<BrandPillar>()
+            .Property(p => p.DescriptionEmbedding)
+            .HasColumnType($"vector({Dimensions})")
+            .HasConversion(
+                v => v == null ? null : new Vector(v),
+                v => v == null ? null : v.ToArray());
+    }
+}
diff --git a/src/PBA.Infrastructure/DependencyInjection.cs b/src/PBA.Infrastructure/DependencyInjection.cs
index 8c01c18..5404f52 100644
--- a/src/PBA.Infrastructure/DependencyInjection.cs
+++ b/src/PBA.Infrastructure/DependencyInjection.cs
@@ -115,6 +115,7 @@ public static class DependencyInjection
 
         services.AddScoped<IFeedSeedService, FeedSeedService>();
         services.AddScoped<IIdeaSourceSeedService, IdeaSourceSeedService>();
+        services.AddScoped<IBrandRankingProfileSeedService, BrandRankingProfileSeedService>();
 
         services.Configure<GoogleAnalyticsOptions>(
             configuration.GetSection(GoogleAnalyticsOptions.SectionName));
diff --git a/src/PBA.Infrastructure/Seeding/BrandRankingProfileSeedService.cs b/src/PBA.Infrastructure/Seeding/BrandRankingProfileSeedService.cs
new file mode 100644
index 0000000..0cdc772
--- /dev/null
+++ b/src/PBA.Infrastructure/Seeding/BrandRankingProfileSeedService.cs
@@ -0,0 +1,111 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+
+namespace PBA.Infrastructure.Seeding;
+
+/// <summary>
+/// Seeds the v1 active BrandRankingProfile from planning/brand-strategy/brand-profile-v0.md.
+/// Idempotent (no-op if an active profile exists) and race-safe (a concurrent host losing the
+/// partial-unique-index race is swallowed, not fatal at startup) — R-L5.
+/// </summary>
+public sealed class BrandRankingProfileSeedService(IAppDbContext db) : IBrandRankingProfileSeedService
+{
+    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
+    {
+        if (await db.BrandRankingProfiles.AnyAsync(p => p.IsActive, cancellationToken))
+            return 0;
+
+        db.BrandRankingProfiles.Add(BuildV1());
+
+        try
+        {
+            await db.SaveChangesAsync(cancellationToken);
+            return 1;
+        }
+        catch (DbUpdateException)
+        {
+            // Another host won the single-active partial-unique-index race; the profile exists. No-op.
+            return 0;
+        }
+    }
+
+    private static BrandRankingProfile BuildV1()
+    {
+        var now = DateTimeOffset.UtcNow;
+        return new BrandRankingProfile
+        {
+            Version = 1,
+            IsActive = true,
+            UpdatedAt = now,
+            Positioning = "I show enterprise teams what AI can actually ship - by building it myself.",
+            AudiencePrimary =
+                "Senior engineers, EMs, and architects at enterprise companies who are frustrated by AI " +
+                "hype and want proof, patterns, and shipped artifacts.",
+            AudienceSecondary = "Enterprise executives and decision-makers.",
+            HalfLifeDays = 7,
+            DecayFloor = 0.075,
+            AntiTopicMultiplier = 0.1,
+            AuthorityBoost = 1.2,
+            Pillars =
+            [
+                new BrandPillar
+                {
+                    Name = "Agent-Native Architecture", Weight = 0.28, Order = 0,
+                    Description = "Harnesses, Agent Skills, MCP, context engineering, agent memory, " +
+                        "multi-agent patterns, plus AI economics/tokenomics/cost.",
+                },
+                new BrandPillar
+                {
+                    Name = "Enterprise AI Adoption & Governance", Weight = 0.23, Order = 1,
+                    Description = "Exec-altitude: strategy, operating model, governance, ROI, the " +
+                        "95%-failure framing.",
+                },
+                new BrandPillar
+                {
+                    Name = "Agentic SDLC & Eng Transformation", Weight = 0.22, Order = 2,
+                    Description = "How engineering orgs actually build with agents; orchestrate-not-" +
+                        "implement; training.",
+                },
+                new BrandPillar
+                {
+                    Name = "Claude / Anthropic Agent Engineering", Weight = 0.15, Order = 3,
+                    Description = "Bringing Anthropic/Claude-Code patterns into the enterprise; Agent " +
+                        "Skills, Claude Code architecture, the MD-who-ships-on-Claude position.",
+                },
+                new BrandPillar
+                {
+                    Name = "Microsoft Enterprise AI Stack", Weight = 0.12, Order = 4,
+                    Description = "Foundry, Copilot, Agent Framework, .NET/C# in enterprise AI.",
+                },
+            ],
+            AuthorityTopics =
+            [
+                ".NET / C# in enterprise AI",
+                "Anthropic Agent Skills / Claude Code architecture",
+                "Claude-Code-style agent harnesses",
+                "Agent Skills framework",
+                "MCP server design",
+                "Agentic SDLC tooling",
+            ],
+            AntiTopics =
+            [
+                "Digital-human / avatar / talking-head tech",
+                "Consumer-AI gossip / product drama",
+                "Model-release horse-race / benchmark-leaderboard news",
+                "Crypto / web3 / AI-token coins",
+                "AGI philosophy / doomerism",
+                "Funding-round / VC news with no enterprise-build angle",
+                "Pure consumer dev tutorials with no enterprise constraint",
+            ],
+            VoiceMarkers =
+            [
+                "Simple enough for execs, technical enough to not be fluff",
+                "Contrarian / false-debate debunking",
+                "Data > opinion - specific numbers",
+                "Every claim has a build behind it (Mollick rule)",
+                "No em dashes, no AI-isms, no promotional inflation",
+            ],
+        };
+    }
+}
diff --git a/tests/PBA.Application.Tests/Domain/BrandRankingProfileVersioningTests.cs b/tests/PBA.Application.Tests/Domain/BrandRankingProfileVersioningTests.cs
new file mode 100644
index 0000000..f286acd
--- /dev/null
+++ b/tests/PBA.Application.Tests/Domain/BrandRankingProfileVersioningTests.cs
@@ -0,0 +1,138 @@
+using PBA.Domain.Entities;
+using Xunit;
+
+namespace PBA.Application.Tests.FeedRanking;
+
+public class BrandRankingProfileVersioningTests
+{
+    private static BrandRankingProfile Make(out Guid pillarId)
+    {
+        pillarId = Guid.NewGuid();
+        var pid = pillarId;
+        return new BrandRankingProfile
+        {
+            Positioning = "I show enterprise teams what AI can actually ship.",
+            AudiencePrimary = "Senior engineers and architects.",
+            AudienceSecondary = "Enterprise executives.",
+            AuthorityTopics = [".NET in enterprise AI", "MCP server design"],
+            AntiTopics = ["crypto", "AGI doomerism"],
+            VoiceMarkers = ["data > opinion"],
+            Pillars = [new BrandPillar { Id = pid, Name = "Agent-Native", Description = "harnesses, skills, MCP", Weight = 0.5, Order = 0 }],
+            HalfLifeDays = 7,
+            DecayFloor = 0.075,
+            AntiTopicMultiplier = 0.1,
+            AuthorityBoost = 1.2,
+        };
+    }
+
+    private static BrandRankingProfile Clone(BrandRankingProfile p) => new()
+    {
+        Positioning = p.Positioning,
+        AudiencePrimary = p.AudiencePrimary,
+        AudienceSecondary = p.AudienceSecondary,
+        AuthorityTopics = [.. p.AuthorityTopics],
+        AntiTopics = [.. p.AntiTopics],
+        VoiceMarkers = [.. p.VoiceMarkers],
+        Pillars = [.. p.Pillars.Select(x => new BrandPillar { Id = x.Id, Name = x.Name, Description = x.Description, Weight = x.Weight, Order = x.Order })],
+        HalfLifeDays = p.HalfLifeDays,
+        DecayFloor = p.DecayFloor,
+        AntiTopicMultiplier = p.AntiTopicMultiplier,
+        AuthorityBoost = p.AuthorityBoost,
+    };
+
+    [Fact]
+    public void RequiresVersionBump_NoChange_False()
+    {
+        var current = Make(out _);
+        Assert.False(current.RequiresVersionBump(Clone(current)));
+    }
+
+    [Fact]
+    public void RequiresVersionBump_WeightChangeOnly_False()
+    {
+        var current = Make(out _);
+        var proposed = Clone(current);
+        proposed.Pillars[0].Weight = 0.9;
+        Assert.False(current.RequiresVersionBump(proposed));
+    }
+
+    [Theory]
+    [InlineData("halflife")]
+    [InlineData("floor")]
+    [InlineData("antimult")]
+    [InlineData("authboost")]
+    [InlineData("order")]
+    public void RequiresVersionBump_QueryTimeKnobChange_False(string field)
+    {
+        var current = Make(out _);
+        var proposed = Clone(current);
+        switch (field)
+        {
+            case "halflife": proposed.HalfLifeDays = 14; break;
+            case "floor": proposed.DecayFloor = 0.2; break;
+            case "antimult": proposed.AntiTopicMultiplier = 0.05; break;
+            case "authboost": proposed.AuthorityBoost = 1.5; break;
+            case "order": proposed.Pillars[0].Order = 5; break;
+        }
+        Assert.False(current.RequiresVersionBump(proposed));
+    }
+
+    [Fact]
+    public void RequiresVersionBump_PillarNameChange_True()
+    {
+        var current = Make(out _);
+        var proposed = Clone(current);
+        proposed.Pillars[0].Name = "Renamed Pillar";
+        Assert.True(current.RequiresVersionBump(proposed));
+    }
+
+    [Fact]
+    public void RequiresVersionBump_PillarDescriptionChange_True()
+    {
+        var current = Make(out _);
+        var proposed = Clone(current);
+        proposed.Pillars[0].Description = "totally different description";
+        Assert.True(current.RequiresVersionBump(proposed));
+    }
+
+    [Fact]
+    public void RequiresVersionBump_AddPillar_True()
+    {
+        var current = Make(out _);
+        var proposed = Clone(current);
+        proposed.Pillars.Add(new BrandPillar { Id = Guid.NewGuid(), Name = "New", Description = "new", Weight = 0.1, Order = 1 });
+        Assert.True(current.RequiresVersionBump(proposed));
+    }
+
+    [Fact]
+    public void RequiresVersionBump_RemovePillar_True()
+    {
+        var current = Make(out _);
+        var proposed = Clone(current);
+        proposed.Pillars.Clear();
+        Assert.True(current.RequiresVersionBump(proposed));
+    }
+
+    [Theory]
+    [InlineData("positioning")]
+    [InlineData("audienceprimary")]
+    [InlineData("audiencesecondary")]
+    [InlineData("authoritytopics")]
+    [InlineData("antitopics")]
+    [InlineData("voicemarkers")]
+    public void RequiresVersionBump_DefinitionFieldChange_True(string field)
+    {
+        var current = Make(out _);
+        var proposed = Clone(current);
+        switch (field)
+        {
+            case "positioning": proposed.Positioning = "new positioning"; break;
+            case "audienceprimary": proposed.AudiencePrimary = "new audience"; break;
+            case "audiencesecondary": proposed.AudienceSecondary = "new secondary"; break;
+            case "authoritytopics": proposed.AuthorityTopics.Add("new topic"); break;
+            case "antitopics": proposed.AntiTopics.Add("new anti"); break;
+            case "voicemarkers": proposed.VoiceMarkers.Add("new marker"); break;
+        }
+        Assert.True(current.RequiresVersionBump(proposed));
+    }
+}
diff --git a/tests/PBA.Application.Tests/Infrastructure/BrandRankingProfileConfigurationTests.cs b/tests/PBA.Application.Tests/Infrastructure/BrandRankingProfileConfigurationTests.cs
new file mode 100644
index 0000000..9b0b3f3
--- /dev/null
+++ b/tests/PBA.Application.Tests/Infrastructure/BrandRankingProfileConfigurationTests.cs
@@ -0,0 +1,55 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Domain.Entities;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Application.Tests.FeedRanking;
+
+// InMemory-level mapping checks (jsonb lists + owned pillar relationship round-trip). The vector(1536)
+// column, partial unique index, and xmin concurrency are Postgres-only and covered by section-12's
+// Testcontainers run — InMemory cannot model them.
+public class BrandRankingProfileConfigurationTests
+{
+    private static ApplicationDbContext NewContext() =>
+        new(new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString())
+            .Options);
+
+    [Fact]
+    public async Task Profile_RoundTrips_PillarsAndStringLists()
+    {
+        var id = Guid.NewGuid();
+        var pillarId = Guid.NewGuid();
+
+        await using var db = NewContext();
+        db.BrandRankingProfiles.Add(new BrandRankingProfile
+        {
+            Id = id,
+            Version = 1,
+            IsActive = true,
+            Positioning = "pos",
+            AudiencePrimary = "aud",
+            AuthorityTopics = ["a1", "a2"],
+            AntiTopics = ["x1"],
+            VoiceMarkers = ["v1", "v2", "v3"],
+            Pillars =
+            [
+                new BrandPillar { Id = pillarId, Name = "P1", Description = "d1", Weight = 0.6, Order = 0 },
+                new BrandPillar { Name = "P2", Description = "d2", Weight = 0.4, Order = 1 },
+            ],
+        });
+        await db.SaveChangesAsync();
+        db.ChangeTracker.Clear(); // force a fresh load from the store
+
+        var loaded = await db.BrandRankingProfiles.Include(p => p.Pillars).SingleAsync(p => p.Id == id);
+
+        Assert.Equal(["a1", "a2"], loaded.AuthorityTopics);
+        Assert.Equal(["x1"], loaded.AntiTopics);
+        Assert.Equal(3, loaded.VoiceMarkers.Count);
+        Assert.Equal(2, loaded.Pillars.Count);
+        var p1 = loaded.Pillars.Single(p => p.Id == pillarId);
+        Assert.Equal("P1", p1.Name);
+        Assert.Equal(0.6, p1.Weight);
+        Assert.Equal(id, p1.BrandRankingProfileId);
+    }
+}
diff --git a/tests/PBA.Application.Tests/Infrastructure/BrandRankingProfileSeedServiceTests.cs b/tests/PBA.Application.Tests/Infrastructure/BrandRankingProfileSeedServiceTests.cs
new file mode 100644
index 0000000..c217333
--- /dev/null
+++ b/tests/PBA.Application.Tests/Infrastructure/BrandRankingProfileSeedServiceTests.cs
@@ -0,0 +1,75 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Domain.Entities;
+using PBA.Infrastructure.Data;
+using PBA.Infrastructure.Seeding;
+using Xunit;
+
+namespace PBA.Application.Tests.FeedRanking;
+
+public class BrandRankingProfileSeedServiceTests
+{
+    private static ApplicationDbContext NewContext() =>
+        new(new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString())
+            .Options);
+
+    [Fact]
+    public async Task SeedAsync_FirstRun_InsertsV1ActiveProfileWithFivePillars()
+    {
+        await using var db = NewContext();
+        var sut = new BrandRankingProfileSeedService(db);
+
+        var count = await sut.SeedAsync();
+
+        Assert.Equal(1, count);
+        var profile = await db.BrandRankingProfiles.Include(p => p.Pillars).SingleAsync();
+        Assert.True(profile.IsActive);
+        Assert.Equal(1, profile.Version);
+        Assert.Equal(5, profile.Pillars.Count);
+        // v0 weights sum to 1.0
+        Assert.Equal(1.0, profile.Pillars.Sum(p => p.Weight), precision: 6);
+        Assert.Contains(profile.Pillars, p => p.Name == "Agent-Native Architecture" && p.Weight == 0.28);
+        Assert.NotEmpty(profile.AuthorityTopics);
+        Assert.NotEmpty(profile.AntiTopics);
+        Assert.NotEmpty(profile.VoiceMarkers);
+        Assert.Equal(7, profile.HalfLifeDays);
+        Assert.Equal(0.1, profile.AntiTopicMultiplier);
+        Assert.Equal(1.2, profile.AuthorityBoost);
+    }
+
+    [Fact]
+    public async Task SeedAsync_SecondRun_IsNoOp()
+    {
+        await using var db = NewContext();
+        var sut = new BrandRankingProfileSeedService(db);
+        await sut.SeedAsync();
+        var first = await db.BrandRankingProfiles.SingleAsync();
+
+        var secondCount = await sut.SeedAsync();
+
+        Assert.Equal(0, secondCount);
+        var all = await db.BrandRankingProfiles.ToListAsync();
+        Assert.Single(all);
+        Assert.Equal(first.Id, all[0].Id);
+    }
+
+    [Fact]
+    public async Task SeedAsync_WhenActiveProfileAlreadyExists_DoesNotInsert()
+    {
+        await using var db = NewContext();
+        db.BrandRankingProfiles.Add(new BrandRankingProfile
+        {
+            Version = 1,
+            IsActive = true,
+            Positioning = "pre-existing",
+            AudiencePrimary = "pre-existing",
+        });
+        await db.SaveChangesAsync();
+        var sut = new BrandRankingProfileSeedService(db);
+
+        var count = await sut.SeedAsync();
+
+        Assert.Equal(0, count);
+        Assert.Single(await db.BrandRankingProfiles.ToListAsync());
+    }
+}
