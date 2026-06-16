diff --git a/src/PBA.Api/appsettings.json b/src/PBA.Api/appsettings.json
index d58e070..9cf52a9 100644
--- a/src/PBA.Api/appsettings.json
+++ b/src/PBA.Api/appsettings.json
@@ -44,6 +44,16 @@
     "SiteUrl": "https://matthewkruczek.ai/",
     "CredentialsPath": "secrets/google-analytics-sa.json"
   },
+  "Embedding": {
+    "Model": "openai/text-embedding-3-small",
+    "Dimensions": 1536,
+    "BatchSize": 128
+  },
+  "Ranking": {
+    "PreFilterThreshold": 0.30,
+    "DedupThreshold": 0.85,
+    "ScoringWindowDays": 30
+  },
   "IdeaScoring": {
     "IntervalMinutes": 10,
     "BatchSize": 20,
diff --git a/src/PBA.Application/Common/VectorMath.cs b/src/PBA.Application/Common/VectorMath.cs
new file mode 100644
index 0000000..54e6e83
--- /dev/null
+++ b/src/PBA.Application/Common/VectorMath.cs
@@ -0,0 +1,42 @@
+namespace PBA.Application.Common;
+
+/// <summary>
+/// Pure vector math used by the brand-fit pre-filter, embedding dedup, and read-time ranking.
+/// Lives in the Application layer so it is unit-testable without a database (EF InMemory cannot
+/// execute pgvector SQL, so all vector logic runs in memory through this helper).
+/// </summary>
+public static class VectorMath
+{
+    /// <summary>
+    /// Cosine similarity = dot(a,b) / (||a|| * ||b||). Computes the FULL norm — does NOT assume
+    /// unit-normalized inputs (Matryoshka-shrunk embeddings are not unit-norm, R-M6).
+    /// Returns 0 when either vector is the zero vector (cosine is undefined; never NaN, R-H2).
+    /// Throws <see cref="ArgumentException"/> on length mismatch.
+    /// </summary>
+    public static double CosineSimilarity(float[] a, float[] b)
+    {
+        if (a.Length != b.Length)
+        {
+            throw new ArgumentException(
+                $"Vector length mismatch: {a.Length} vs {b.Length}.", nameof(b));
+        }
+
+        double dot = 0, normA = 0, normB = 0;
+        for (var i = 0; i < a.Length; i++)
+        {
+            double ai = a[i];
+            double bi = b[i];
+            dot += ai * bi;
+            normA += ai * ai;
+            normB += bi * bi;
+        }
+
+        // Cosine vs a zero vector is undefined (0/0 = NaN); contract is 0, never NaN (R-H2).
+        if (normA == 0 || normB == 0)
+        {
+            return 0.0;
+        }
+
+        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
+    }
+}
diff --git a/src/PBA.Infrastructure/Configuration/EmbeddingOptions.cs b/src/PBA.Infrastructure/Configuration/EmbeddingOptions.cs
new file mode 100644
index 0000000..9eefe20
--- /dev/null
+++ b/src/PBA.Infrastructure/Configuration/EmbeddingOptions.cs
@@ -0,0 +1,10 @@
+namespace PBA.Infrastructure.Configuration;
+
+public sealed class EmbeddingOptions
+{
+    public const string SectionName = "Embedding";
+
+    public string Model { get; init; } = "openai/text-embedding-3-small";
+    public int Dimensions { get; init; } = 1536;
+    public int BatchSize { get; init; } = 128;
+}
diff --git a/src/PBA.Infrastructure/Configuration/RankingOptions.cs b/src/PBA.Infrastructure/Configuration/RankingOptions.cs
new file mode 100644
index 0000000..0e9bc61
--- /dev/null
+++ b/src/PBA.Infrastructure/Configuration/RankingOptions.cs
@@ -0,0 +1,21 @@
+namespace PBA.Infrastructure.Configuration;
+
+/// <summary>
+/// Runtime-tunable thresholds for the embedding/scoring pipeline. Consumed via IOptionsMonitor so
+/// the scoring sweep can snapshot CurrentValue once per sweep (R-L2). Half-life / floor / multipliers
+/// deliberately do NOT live here — those are part of the active BrandRankingProfile (DB), applied at
+/// query time so weight/decay edits re-rank without an LLM call.
+/// </summary>
+public sealed class RankingOptions
+{
+    public const string SectionName = "Ranking";
+
+    /// <summary>Brand-fit cutoff an idea must clear to earn an LLM per-pillar scoring call.</summary>
+    public double PreFilterThreshold { get; init; } = 0.30;
+
+    /// <summary>Pairwise cosine at/above which two ideas are treated as duplicates.</summary>
+    public double DedupThreshold { get; init; } = 0.85;
+
+    /// <summary>Rolling window (days) for LLM scoring; older items rank ~0 via recency decay.</summary>
+    public int ScoringWindowDays { get; init; } = 30;
+}
diff --git a/src/PBA.Infrastructure/Data/ApplicationDbContext.cs b/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
index 71c0542..b89e307 100644
--- a/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
+++ b/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
@@ -21,6 +21,7 @@ public class ApplicationDbContext : DbContext, IAppDbContext
 
     protected override void OnModelCreating(ModelBuilder modelBuilder)
     {
+        modelBuilder.HasPostgresExtension("vector"); // pgvector — required by vector(1536) columns
         modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
         base.OnModelCreating(modelBuilder);
     }
diff --git a/src/PBA.Infrastructure/Data/DesignTimeDbContextFactory.cs b/src/PBA.Infrastructure/Data/DesignTimeDbContextFactory.cs
index 8cf4499..ffe678d 100644
--- a/src/PBA.Infrastructure/Data/DesignTimeDbContextFactory.cs
+++ b/src/PBA.Infrastructure/Data/DesignTimeDbContextFactory.cs
@@ -1,5 +1,6 @@
 using Microsoft.EntityFrameworkCore;
 using Microsoft.EntityFrameworkCore.Design;
+using Pgvector.EntityFrameworkCore;
 
 namespace PBA.Infrastructure.Data;
 
@@ -8,7 +9,7 @@ public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<Applicatio
     public ApplicationDbContext CreateDbContext(string[] args)
     {
         var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
-        builder.UseNpgsql("Host=localhost;Database=pba_design_time");
+        builder.UseNpgsql("Host=localhost;Database=pba_design_time", o => o.UseVector());
         return new ApplicationDbContext(builder.Options);
     }
 }
diff --git a/src/PBA.Infrastructure/Data/Migrations/20260616133237_EnablePgVectorExtension.Designer.cs b/src/PBA.Infrastructure/Data/Migrations/20260616133237_EnablePgVectorExtension.Designer.cs
new file mode 100644
index 0000000..632229e
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Migrations/20260616133237_EnablePgVectorExtension.Designer.cs
@@ -0,0 +1,664 @@
+﻿// <auto-generated />
+using System;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Infrastructure;
+using Microsoft.EntityFrameworkCore.Migrations;
+using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
+using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
+using PBA.Infrastructure.Data;
+
+#nullable disable
+
+namespace PBA.Infrastructure.Data.Migrations
+{
+    [DbContext(typeof(ApplicationDbContext))]
+    [Migration("20260616133237_EnablePgVectorExtension")]
+    partial class EnablePgVectorExtension
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
diff --git a/src/PBA.Infrastructure/Data/Migrations/20260616133237_EnablePgVectorExtension.cs b/src/PBA.Infrastructure/Data/Migrations/20260616133237_EnablePgVectorExtension.cs
new file mode 100644
index 0000000..3c218a3
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Migrations/20260616133237_EnablePgVectorExtension.cs
@@ -0,0 +1,24 @@
+﻿using Microsoft.EntityFrameworkCore.Migrations;
+
+#nullable disable
+
+namespace PBA.Infrastructure.Data.Migrations
+{
+    /// <inheritdoc />
+    public partial class EnablePgVectorExtension : Migration
+    {
+        /// <inheritdoc />
+        protected override void Up(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.AlterDatabase()
+                .Annotation("Npgsql:PostgresExtension:vector", ",,");
+        }
+
+        /// <inheritdoc />
+        protected override void Down(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.AlterDatabase()
+                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
+        }
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs b/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
index 222f4c4..a67b6cd 100644
--- a/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
+++ b/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
@@ -20,6 +20,7 @@ namespace PBA.Infrastructure.Data.Migrations
                 .HasAnnotation("ProductVersion", "10.0.7")
                 .HasAnnotation("Relational:MaxIdentifierLength", 63);
 
+            NpgsqlModelBuilderExtensions.HasPostgresExtension(modelBuilder, "vector");
             NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
 
             modelBuilder.Entity("PBA.Domain.Entities.BrandProfile", b =>
diff --git a/src/PBA.Infrastructure/DependencyInjection.cs b/src/PBA.Infrastructure/DependencyInjection.cs
index 9a69c3c..8c01c18 100644
--- a/src/PBA.Infrastructure/DependencyInjection.cs
+++ b/src/PBA.Infrastructure/DependencyInjection.cs
@@ -13,6 +13,8 @@ using PBA.Infrastructure.Seeding;
 using PBA.Infrastructure.Security;
 using PBA.Infrastructure.Services;
 using PBA.Infrastructure.Transformers;
+using Npgsql;
+using Pgvector.EntityFrameworkCore;
 
 namespace PBA.Infrastructure;
 
@@ -25,12 +27,15 @@ public static class DependencyInjection
         var connectionString = configuration.GetConnectionString("DefaultConnection");
         var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
         dataSourceBuilder.EnableDynamicJson();
+        dataSourceBuilder.UseVector(); // pgvector type mapping at the Npgsql data-source level
         var dataSource = dataSourceBuilder.Build();
 
         services.AddDbContext<ApplicationDbContext>(options =>
             options.UseNpgsql(
                 dataSource,
-                npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));
+                npgsql => npgsql
+                    .UseVector()
+                    .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));
 
         services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
 
@@ -74,6 +79,10 @@ public static class DependencyInjection
         services.AddHttpClient<ISidecarClient, OpenRouterClient>();
         services.AddHostedService<AiConnectionsService>();
 
+        // Brand-anchored feed ranking: embedding model + runtime-tunable ranking thresholds.
+        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
+        services.Configure<RankingOptions>(configuration.GetSection(RankingOptions.SectionName));
+
         // AI News Radar (Horizon-inspired): scoring -> clustering -> daily digest.
         services.Configure<IdeaScoringOptions>(configuration.GetSection(IdeaScoringOptions.SectionName));
         services.Configure<ClusteringOptions>(configuration.GetSection(ClusteringOptions.SectionName));
diff --git a/src/PBA.Infrastructure/PBA.Infrastructure.csproj b/src/PBA.Infrastructure/PBA.Infrastructure.csproj
index 39b4265..cdf8b69 100644
--- a/src/PBA.Infrastructure/PBA.Infrastructure.csproj
+++ b/src/PBA.Infrastructure/PBA.Infrastructure.csproj
@@ -28,6 +28,7 @@
     <PackageReference Include="Hangfire.Core" Version="1.8.17" />
     <PackageReference Include="Hangfire.PostgreSql" Version="1.20.10" />
     <PackageReference Include="Markdig" Version="0.40.0" />
+    <PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />
     <PackageReference Include="System.ServiceModel.Syndication" Version="10.0.0" />
   </ItemGroup>
 
diff --git a/tests/PBA.Application.Tests/Common/VectorMathTests.cs b/tests/PBA.Application.Tests/Common/VectorMathTests.cs
new file mode 100644
index 0000000..3a5e2f4
--- /dev/null
+++ b/tests/PBA.Application.Tests/Common/VectorMathTests.cs
@@ -0,0 +1,78 @@
+using PBA.Application.Common;
+using Xunit;
+
+namespace PBA.Application.Tests.Common;
+
+public class VectorMathTests
+{
+    [Fact]
+    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
+    {
+        var a = new[] { 1f, 2f, 3f };
+        var b = new[] { 1f, 2f, 3f };
+
+        Assert.Equal(1.0, VectorMath.CosineSimilarity(a, b), precision: 6);
+    }
+
+    [Fact]
+    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
+    {
+        var a = new[] { 1f, 0f };
+        var b = new[] { 0f, 1f };
+
+        Assert.Equal(0.0, VectorMath.CosineSimilarity(a, b), precision: 6);
+    }
+
+    [Fact]
+    public void CosineSimilarity_OppositeVectors_ReturnsMinusOne()
+    {
+        var a = new[] { 1f, 2f, 3f };
+        var b = new[] { -1f, -2f, -3f };
+
+        Assert.Equal(-1.0, VectorMath.CosineSimilarity(a, b), precision: 6);
+    }
+
+    [Theory]
+    // Scale-invariant: b is a positive multiple of a => cosine 1.0 even though norms differ (R-M6).
+    [InlineData(new[] { 1f, 2f, 3f }, new[] { 2f, 4f, 6f }, 1.0)]
+    // Non-unit-length inputs with a known non-trivial cosine: [1,0] vs [1,1] => 1/sqrt(2).
+    [InlineData(new[] { 1f, 0f }, new[] { 1f, 1f }, 0.70710678)]
+    public void CosineSimilarity_NonNormalizedInputs_ComputesFullNorm(float[] a, float[] b, double expected)
+    {
+        // A naive dot-product-only implementation (assuming unit norm) would fail these.
+        Assert.Equal(expected, VectorMath.CosineSimilarity(a, b), precision: 6);
+    }
+
+    [Fact]
+    public void CosineSimilarity_ZeroVector_ReturnsZeroNeverNaN()
+    {
+        var a = new[] { 0f, 0f, 0f };
+        var b = new[] { 1f, 2f, 3f };
+
+        var result = VectorMath.CosineSimilarity(a, b);
+
+        Assert.Equal(0.0, result);
+        Assert.False(double.IsNaN(result));
+    }
+
+    [Fact]
+    public void CosineSimilarity_BothZeroVectors_ReturnsZeroNeverNaN()
+    {
+        var a = new[] { 0f, 0f };
+        var b = new[] { 0f, 0f };
+
+        var result = VectorMath.CosineSimilarity(a, b);
+
+        Assert.Equal(0.0, result);
+        Assert.False(double.IsNaN(result));
+    }
+
+    [Fact]
+    public void CosineSimilarity_LengthMismatch_Throws()
+    {
+        var a = new[] { 1f, 2f, 3f };
+        var b = new[] { 1f, 2f };
+
+        Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(a, b));
+    }
+}
