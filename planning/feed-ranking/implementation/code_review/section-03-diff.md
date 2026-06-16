diff --git a/src/PBA.Domain/Entities/Idea.cs b/src/PBA.Domain/Entities/Idea.cs
index 7796650..b0645fe 100644
--- a/src/PBA.Domain/Entities/Idea.cs
+++ b/src/PBA.Domain/Entities/Idea.cs
@@ -25,6 +25,16 @@ public class Idea
     public DateTimeOffset? ClusteredAt { get; set; }
     public DateTimeOffset? AlertedAt { get; set; }
 
+    // Brand-anchored ranking (feed-ranking redesign) -----------------------------------------
+    public float[]? Embedding { get; set; }                          // vector(1536), null until embedded
+    public DateTimeOffset? EmbeddedAt { get; set; }
+    public IList<PillarSubScore> PillarSubScores { get; set; } = []; // jsonb, raw 0..1 per pillar (R-C3)
+    public bool? IsAntiTopic { get; set; }
+    public bool? IsAuthorityTopic { get; set; }
+    public int? ScoredProfileVersion { get; set; }                   // BrandRankingProfile.Version used (R-H3)
+    public int ScoreAttempts { get; set; }                           // sweep caps to avoid poison-item burn (R-M5)
+    // Score (existing int? 0-10) is retained as a derived display value = round(brandFit*10).
+
     public IdeaSource? IdeaSource { get; set; }
     public SavedIdea? SavedDetails { get; set; }
 }
diff --git a/src/PBA.Domain/Entities/PillarSubScore.cs b/src/PBA.Domain/Entities/PillarSubScore.cs
new file mode 100644
index 0000000..ca20274
--- /dev/null
+++ b/src/PBA.Domain/Entities/PillarSubScore.cs
@@ -0,0 +1,14 @@
+namespace PBA.Domain.Entities;
+
+/// <summary>
+/// One pillar's raw fit score for an idea, stored as part of the Idea's jsonb PillarSubScores document.
+/// Keyed by <see cref="PillarId"/> (== BrandPillar.Id, R-C3) so it survives pillar renames;
+/// <see cref="PillarName"/> is display-only and may go stale.
+/// </summary>
+public sealed class PillarSubScore
+{
+    public Guid PillarId { get; set; }
+    public string PillarName { get; set; } = string.Empty;
+    public double Score { get; set; }          // raw 0..1
+    public string? Reason { get; set; }        // one-line LLM rationale
+}
diff --git a/src/PBA.Infrastructure/Data/Configurations/IdeaConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/IdeaConfiguration.cs
index 630c17f..87149c6 100644
--- a/src/PBA.Infrastructure/Data/Configurations/IdeaConfiguration.cs
+++ b/src/PBA.Infrastructure/Data/Configurations/IdeaConfiguration.cs
@@ -1,5 +1,8 @@
+using System.Text.Json;
 using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.ChangeTracking;
 using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
 using PBA.Domain.Entities;
 
 namespace PBA.Infrastructure.Data.Configurations;
@@ -30,10 +33,33 @@ public class IdeaConfiguration : IEntityTypeConfiguration<Idea>
 
         builder.Property(i => i.ScoreReason).HasColumnType("text");
 
+        // Feed-ranking columns. PillarSubScores is a JSON document (NOT a join table): brandFit is
+        // computed in the read handler in memory, never aggregated in SQL, so a relational table buys
+        // nothing. Keyed by BrandPillarId (R-C3). A collection of a COMPLEX type isn't mappable by the
+        // InMemory provider, so it goes through an explicit JSON string converter (stored as jsonb on
+        // Npgsql, as a string on InMemory) rather than relying on Npgsql dynamic JSON. Embedding maps to
+        // vector(1536) in PgVectorModelConfiguration (Npgsql-only).
+        var subScoreConverter = new ValueConverter<IList<PillarSubScore>, string>(
+            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
+            v => JsonSerializer.Deserialize<List<PillarSubScore>>(v, (JsonSerializerOptions?)null)
+                 ?? new List<PillarSubScore>());
+        var subScoreComparer = new ValueComparer<IList<PillarSubScore>>(
+            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
+                      == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
+            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
+            v => JsonSerializer.Deserialize<List<PillarSubScore>>(
+                     JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!);
+        builder.Property(i => i.PillarSubScores)
+            .HasColumnType("jsonb")
+            .HasConversion(subScoreConverter, subScoreComparer);
+
+        builder.Property(i => i.ScoreAttempts).HasDefaultValue(0);
+
         builder.HasIndex(i => i.ScoredAt);
         builder.HasIndex(i => i.Score);
         builder.HasIndex(i => i.DuplicateOfId);
         builder.HasIndex(i => i.AlertedAt);
+        builder.HasIndex(i => i.ScoredProfileVersion);
 
         builder.HasOne<Idea>()
             .WithMany()
diff --git a/src/PBA.Infrastructure/Data/Migrations/20260616151100_AddIdeaEmbeddingAndSubScores.Designer.cs b/src/PBA.Infrastructure/Data/Migrations/20260616151100_AddIdeaEmbeddingAndSubScores.Designer.cs
new file mode 100644
index 0000000..ab67426
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Migrations/20260616151100_AddIdeaEmbeddingAndSubScores.Designer.cs
@@ -0,0 +1,806 @@
+﻿// <auto-generated />
+using System;
+using System.Collections.Generic;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Infrastructure;
+using Microsoft.EntityFrameworkCore.Migrations;
+using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
+using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
+using PBA.Domain.Entities;
+using PBA.Infrastructure.Data;
+using Pgvector;
+
+#nullable disable
+
+namespace PBA.Infrastructure.Data.Migrations
+{
+    [DbContext(typeof(ApplicationDbContext))]
+    [Migration("20260616151100_AddIdeaEmbeddingAndSubScores")]
+    partial class AddIdeaEmbeddingAndSubScores
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
+                    b.Property<IList<PillarSubScore>>("PillarSubScores")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
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
diff --git a/src/PBA.Infrastructure/Data/Migrations/20260616151100_AddIdeaEmbeddingAndSubScores.cs b/src/PBA.Infrastructure/Data/Migrations/20260616151100_AddIdeaEmbeddingAndSubScores.cs
new file mode 100644
index 0000000..8a3d9bf
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Migrations/20260616151100_AddIdeaEmbeddingAndSubScores.cs
@@ -0,0 +1,102 @@
+﻿using System;
+using System.Collections.Generic;
+using Microsoft.EntityFrameworkCore.Migrations;
+using PBA.Domain.Entities;
+using Pgvector;
+
+#nullable disable
+
+namespace PBA.Infrastructure.Data.Migrations
+{
+    /// <inheritdoc />
+    public partial class AddIdeaEmbeddingAndSubScores : Migration
+    {
+        /// <inheritdoc />
+        protected override void Up(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.AddColumn<DateTimeOffset>(
+                name: "EmbeddedAt",
+                table: "Ideas",
+                type: "timestamp with time zone",
+                nullable: true);
+
+            migrationBuilder.AddColumn<Vector>(
+                name: "Embedding",
+                table: "Ideas",
+                type: "vector(1536)",
+                nullable: true);
+
+            migrationBuilder.AddColumn<bool>(
+                name: "IsAntiTopic",
+                table: "Ideas",
+                type: "boolean",
+                nullable: true);
+
+            migrationBuilder.AddColumn<bool>(
+                name: "IsAuthorityTopic",
+                table: "Ideas",
+                type: "boolean",
+                nullable: true);
+
+            migrationBuilder.AddColumn<IList<PillarSubScore>>(
+                name: "PillarSubScores",
+                table: "Ideas",
+                type: "jsonb",
+                nullable: false);
+
+            migrationBuilder.AddColumn<int>(
+                name: "ScoreAttempts",
+                table: "Ideas",
+                type: "integer",
+                nullable: false,
+                defaultValue: 0);
+
+            migrationBuilder.AddColumn<int>(
+                name: "ScoredProfileVersion",
+                table: "Ideas",
+                type: "integer",
+                nullable: true);
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Ideas_ScoredProfileVersion",
+                table: "Ideas",
+                column: "ScoredProfileVersion");
+        }
+
+        /// <inheritdoc />
+        protected override void Down(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.DropIndex(
+                name: "IX_Ideas_ScoredProfileVersion",
+                table: "Ideas");
+
+            migrationBuilder.DropColumn(
+                name: "EmbeddedAt",
+                table: "Ideas");
+
+            migrationBuilder.DropColumn(
+                name: "Embedding",
+                table: "Ideas");
+
+            migrationBuilder.DropColumn(
+                name: "IsAntiTopic",
+                table: "Ideas");
+
+            migrationBuilder.DropColumn(
+                name: "IsAuthorityTopic",
+                table: "Ideas");
+
+            migrationBuilder.DropColumn(
+                name: "PillarSubScores",
+                table: "Ideas");
+
+            migrationBuilder.DropColumn(
+                name: "ScoreAttempts",
+                table: "Ideas");
+
+            migrationBuilder.DropColumn(
+                name: "ScoredProfileVersion",
+                table: "Ideas");
+        }
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs b/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
index 9e9154b..1447b8c 100644
--- a/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
+++ b/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
@@ -1,9 +1,11 @@
 ﻿// <auto-generated />
 using System;
+using System.Collections.Generic;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.EntityFrameworkCore.Infrastructure;
 using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
 using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
+using PBA.Domain.Entities;
 using PBA.Infrastructure.Data;
 using Pgvector;
 
@@ -463,18 +465,42 @@ namespace PBA.Infrastructure.Data.Migrations
                     b.Property<Guid?>("DuplicateOfId")
                         .HasColumnType("uuid");
 
+                    b.Property<DateTimeOffset?>("EmbeddedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Vector>("Embedding")
+                        .HasColumnType("vector(1536)");
+
                     b.Property<Guid?>("IdeaSourceId")
                         .HasColumnType("uuid");
 
+                    b.Property<bool?>("IsAntiTopic")
+                        .HasColumnType("boolean");
+
+                    b.Property<bool?>("IsAuthorityTopic")
+                        .HasColumnType("boolean");
+
+                    b.Property<IList<PillarSubScore>>("PillarSubScores")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
                     b.Property<int?>("Score")
                         .HasColumnType("integer");
 
+                    b.Property<int>("ScoreAttempts")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
                     b.Property<string>("ScoreReason")
                         .HasColumnType("text");
 
                     b.Property<DateTimeOffset?>("ScoredAt")
                         .HasColumnType("timestamp with time zone");
 
+                    b.Property<int?>("ScoredProfileVersion")
+                        .HasColumnType("integer");
+
                     b.Property<string>("SourceName")
                         .IsRequired()
                         .HasMaxLength(200)
@@ -517,6 +543,8 @@ namespace PBA.Infrastructure.Data.Migrations
 
                     b.HasIndex("ScoredAt");
 
+                    b.HasIndex("ScoredProfileVersion");
+
                     b.ToTable("Ideas");
                 });
 
diff --git a/src/PBA.Infrastructure/Data/PgVectorModelConfiguration.cs b/src/PBA.Infrastructure/Data/PgVectorModelConfiguration.cs
index 5aac187..7f59c3c 100644
--- a/src/PBA.Infrastructure/Data/PgVectorModelConfiguration.cs
+++ b/src/PBA.Infrastructure/Data/PgVectorModelConfiguration.cs
@@ -26,5 +26,12 @@ internal static class PgVectorModelConfiguration
             .HasConversion(
                 v => v == null ? null : new Vector(v),
                 v => v == null ? null : v.ToArray());
+
+        modelBuilder.Entity<Idea>()
+            .Property(i => i.Embedding)
+            .HasColumnType($"vector({Dimensions})")
+            .HasConversion(
+                v => v == null ? null : new Vector(v),
+                v => v == null ? null : v.ToArray());
     }
 }
diff --git a/tests/PBA.Application.Tests/Infrastructure/IdeaRankingFieldsConfigurationTests.cs b/tests/PBA.Application.Tests/Infrastructure/IdeaRankingFieldsConfigurationTests.cs
new file mode 100644
index 0000000..66970fb
--- /dev/null
+++ b/tests/PBA.Application.Tests/Infrastructure/IdeaRankingFieldsConfigurationTests.cs
@@ -0,0 +1,82 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Application.Tests.FeedRanking;
+
+// InMemory checks for the new ranking fields on Idea (round-trip + ScoreAttempts default). The
+// vector(1536) Embedding column and the literal jsonb column type are Postgres-only and covered by
+// section-12 Testcontainers — InMemory stores PillarSubScores/float[] natively.
+public class IdeaRankingFieldsConfigurationTests
+{
+    private static ApplicationDbContext NewContext() =>
+        new(new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString())
+            .Options);
+
+    private static Idea NewIdea() => new()
+    {
+        Title = "t",
+        SourceName = "s",
+        DeduplicationKey = Guid.NewGuid().ToString(),
+        Status = IdeaStatus.New,
+        DetectedAt = DateTimeOffset.UtcNow,
+    };
+
+    [Fact]
+    public async Task NewIdea_ScoreAttempts_DefaultsToZero_AndRankingFieldsNullByDefault()
+    {
+        var id = Guid.NewGuid();
+        await using var db = NewContext();
+        var idea = NewIdea();
+        idea.GetType().GetProperty("Id")!.SetValue(idea, id);
+        db.Ideas.Add(idea);
+        await db.SaveChangesAsync();
+        db.ChangeTracker.Clear();
+
+        var loaded = await db.Ideas.SingleAsync(i => i.Id == id);
+        Assert.Equal(0, loaded.ScoreAttempts);
+        Assert.Null(loaded.Embedding);
+        Assert.Null(loaded.EmbeddedAt);
+        Assert.Null(loaded.IsAntiTopic);
+        Assert.Null(loaded.IsAuthorityTopic);
+        Assert.Null(loaded.ScoredProfileVersion);
+        Assert.Empty(loaded.PillarSubScores);
+    }
+
+    [Fact]
+    public async Task RankingFields_RoundTrip_IncludingPillarSubScoresByPillarId()
+    {
+        var id = Guid.NewGuid();
+        var pillarId = Guid.NewGuid();
+        await using var db = NewContext();
+        var idea = NewIdea();
+        idea.GetType().GetProperty("Id")!.SetValue(idea, id);
+        idea.EmbeddedAt = DateTimeOffset.UtcNow;
+        idea.IsAntiTopic = false;
+        idea.IsAuthorityTopic = true;
+        idea.ScoredProfileVersion = 1;
+        idea.ScoreAttempts = 2;
+        idea.PillarSubScores =
+        [
+            new PillarSubScore { PillarId = pillarId, PillarName = "P1", Score = 0.75, Reason = "fit" },
+            new PillarSubScore { PillarId = Guid.NewGuid(), PillarName = "P2", Score = 0.25 },
+        ];
+        db.Ideas.Add(idea);
+        await db.SaveChangesAsync();
+        db.ChangeTracker.Clear();
+
+        var loaded = await db.Ideas.SingleAsync(i => i.Id == id);
+        Assert.True(loaded.IsAuthorityTopic);
+        Assert.False(loaded.IsAntiTopic);
+        Assert.Equal(1, loaded.ScoredProfileVersion);
+        Assert.Equal(2, loaded.ScoreAttempts);
+        Assert.Equal(2, loaded.PillarSubScores.Count);
+        var p1 = loaded.PillarSubScores.Single(s => s.PillarId == pillarId);
+        Assert.Equal(0.75, p1.Score);
+        Assert.Equal("fit", p1.Reason);
+        Assert.Equal("P1", p1.PillarName);
+    }
+}
