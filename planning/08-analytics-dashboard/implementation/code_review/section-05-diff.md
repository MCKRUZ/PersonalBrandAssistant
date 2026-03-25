diff --git a/src/PersonalBrandAssistant.Api/appsettings.json b/src/PersonalBrandAssistant.Api/appsettings.json
index 0199e5e..e7e090e 100644
--- a/src/PersonalBrandAssistant.Api/appsettings.json
+++ b/src/PersonalBrandAssistant.Api/appsettings.json
@@ -117,5 +117,13 @@
     },
     "PlatformPrompts": {}
   },
+  "GoogleAnalytics": {
+    "CredentialsPath": "secrets/google-analytics-sa.json",
+    "PropertyId": "261358185",
+    "SiteUrl": "https://matthewkruczek.ai/"
+  },
+  "Substack": {
+    "FeedUrl": "https://matthewkruczek.substack.com/feed"
+  },
   "AllowedHosts": "*"
 }
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IDashboardCacheInvalidator.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IDashboardCacheInvalidator.cs
new file mode 100644
index 0000000..204ff02
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IDashboardCacheInvalidator.cs
@@ -0,0 +1,7 @@
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+/// <summary>Allows cache invalidation for dashboard data with rate limiting.</summary>
+public interface IDashboardCacheInvalidator
+{
+    Task<bool> TryInvalidateAsync(CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/AnalyticsHealthStatus.cs b/src/PersonalBrandAssistant.Application/Common/Models/AnalyticsHealthStatus.cs
new file mode 100644
index 0000000..b843834
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/AnalyticsHealthStatus.cs
@@ -0,0 +1,8 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+/// <summary>Connectivity status for an external analytics data source.</summary>
+public record AnalyticsHealthStatus(
+    string Source,
+    bool IsHealthy,
+    string? LastError,
+    DateTimeOffset? LastSuccessAt);
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs
index 8602ee6..5653283 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs
@@ -25,6 +25,10 @@ public class ContentPlatformStatusConfiguration : IEntityTypeConfiguration<Conte
         builder.HasIndex(c => new { c.ContentId, c.Platform });
         builder.HasIndex(c => c.IdempotencyKey).IsUnique();
 
+        // Dashboard query index: count published content per platform in a date range
+        builder.HasIndex(c => new { c.PublishedAt, c.Platform })
+            .HasFilter("\"PublishedAt\" IS NOT NULL");
+
         builder.HasOne<Content>()
             .WithMany()
             .HasForeignKey(c => c.ContentId)
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs
index dc2c492..0d14f7f 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs
@@ -21,6 +21,9 @@ public class EngagementSnapshotConfiguration : IEntityTypeConfiguration<Engageme
         builder.HasIndex(e => new { e.ContentPlatformStatusId, e.FetchedAt })
             .IsDescending(false, true);
 
+        // Reverse-order index for dashboard timeline queries that filter by FetchedAt first
+        builder.HasIndex(e => new { e.FetchedAt, e.ContentPlatformStatusId });
+
         builder.HasOne<ContentPlatformStatus>()
             .WithMany()
             .HasForeignKey(e => e.ContentPlatformStatusId)
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index 4dd406a..74320fb 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -1,8 +1,13 @@
+using System.Net;
 using Microsoft.AspNetCore.DataProtection;
 using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Caching.Hybrid;
 using Microsoft.Extensions.Configuration;
 using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Http.Resilience;
+using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
+using Polly;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
 using PersonalBrandAssistant.Infrastructure.Agents;
@@ -137,6 +142,14 @@ public static class DependencyInjection
         // Singleton services
         services.AddSingleton(TimeProvider.System);
         services.AddMemoryCache();
+        services.AddHybridCache(options =>
+        {
+            options.DefaultEntryOptions = new HybridCacheEntryOptions
+            {
+                Expiration = TimeSpan.FromMinutes(30),
+                LocalCacheExpiration = TimeSpan.FromMinutes(5)
+            };
+        });
         services.AddSingleton<IMediaStorage, LocalMediaStorage>();
 
         // Platform adapters with typed HttpClients
@@ -239,9 +252,28 @@ public static class DependencyInjection
             configuration.GetSection(SubstackOptions.SectionName));
         services.AddHttpClient<ISubstackService, SubstackService>(client =>
         {
-            client.Timeout = TimeSpan.FromSeconds(10);
+            client.Timeout = Timeout.InfiniteTimeSpan; // Polly handles timeouts
             client.DefaultRequestHeaders.UserAgent.ParseAdd(
                 "PersonalBrandAssistant/1.0 (+https://github.com/MCKRUZ/personal-brand-assistant)");
+        })
+        .AddStandardResilienceHandler(options =>
+        {
+            options.Retry.MaxRetryAttempts = 2;
+            options.Retry.UseJitter = true;
+            options.Retry.BackoffType = DelayBackoffType.Exponential;
+            options.Retry.ShouldHandle = args => ValueTask.FromResult(
+                args.Outcome.Result?.StatusCode is HttpStatusCode.TooManyRequests
+                or HttpStatusCode.ServiceUnavailable
+                or HttpStatusCode.InternalServerError
+                or HttpStatusCode.BadGateway
+                or HttpStatusCode.GatewayTimeout
+                || args.Outcome.Exception is HttpRequestException or TaskCanceledException);
+            options.CircuitBreaker.FailureRatio = 1.0;
+            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
+            options.CircuitBreaker.MinimumThroughput = 3;
+            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
+            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
+            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
         });
 
         // Google Analytics / Search Console
@@ -276,7 +308,15 @@ public static class DependencyInjection
             return new SearchConsoleClientWrapper(service);
         });
         services.AddScoped<IGoogleAnalyticsService, GoogleAnalyticsService>();
-        services.AddScoped<IDashboardAggregator, DashboardAggregator>();
+        services.AddScoped<DashboardAggregator>();
+        services.AddScoped<IDashboardAggregator>(sp =>
+            new CachedDashboardAggregator(
+                sp.GetRequiredService<DashboardAggregator>(),
+                sp.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>(),
+                sp.GetRequiredService<ILogger<CachedDashboardAggregator>>(),
+                sp.GetRequiredService<TimeProvider>()));
+        services.AddScoped<IDashboardCacheInvalidator>(sp =>
+            (CachedDashboardAggregator)sp.GetRequiredService<IDashboardAggregator>());
 
         // Content automation
         services.Configure<ContentAutomationOptions>(
diff --git a/src/PersonalBrandAssistant.Infrastructure/Migrations/20260316193800_InitialCreate.Designer.cs b/src/PersonalBrandAssistant.Infrastructure/Migrations/20260316193800_InitialCreate.Designer.cs
new file mode 100644
index 0000000..84f1af2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Migrations/20260316193800_InitialCreate.Designer.cs
@@ -0,0 +1,1119 @@
+﻿// <auto-generated />
+using System;
+using System.Collections.Generic;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Infrastructure;
+using Microsoft.EntityFrameworkCore.Migrations;
+using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
+using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
+using PersonalBrandAssistant.Infrastructure.Data;
+
+#nullable disable
+
+namespace PersonalBrandAssistant.Infrastructure.Migrations
+{
+    [DbContext(typeof(ApplicationDbContext))]
+    [Migration("20260316193800_InitialCreate")]
+    partial class InitialCreate
+    {
+        /// <inheritdoc />
+        protected override void BuildTargetModel(ModelBuilder modelBuilder)
+        {
+#pragma warning disable 612, 618
+            modelBuilder
+                .HasAnnotation("ProductVersion", "10.0.5")
+                .HasAnnotation("Relational:MaxIdentifierLength", 63);
+
+            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecution", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("AgentType")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("CacheCreationTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("CacheReadTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset?>("CompletedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid?>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<decimal>("Cost")
+                        .HasPrecision(18, 6)
+                        .HasColumnType("numeric(18,6)");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<TimeSpan?>("Duration")
+                        .HasColumnType("interval");
+
+                    b.Property<string>("Error")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<int>("InputTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<string>("ModelId")
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<int>("ModelUsed")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("OutputSummary")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<int>("OutputTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("StartedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentId");
+
+                    b.HasIndex("Status", "AgentType");
+
+                    b.ToTable("AgentExecutions", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecutionLog", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("AgentExecutionId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Content")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<int>("StepNumber")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("StepType")
+                        .IsRequired()
+                        .HasMaxLength(50)
+                        .HasColumnType("character varying(50)");
+
+                    b.Property<DateTimeOffset>("Timestamp")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("TokensUsed")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("AgentExecutionId");
+
+                    b.ToTable("AgentExecutionLogs", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AuditLogEntry", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Action")
+                        .IsRequired()
+                        .HasMaxLength(50)
+                        .HasColumnType("character varying(50)");
+
+                    b.Property<string>("Details")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<Guid>("EntityId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("EntityType")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("NewValue")
+                        .HasColumnType("text");
+
+                    b.Property<string>("OldValue")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset>("Timestamp")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Timestamp");
+
+                    b.ToTable("AuditLogEntries", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AutonomyConfiguration", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ContentTypeOverrides")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("ContentTypePlatformOverrides")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("GlobalLevel")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("PlatformOverrides")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.ToTable("AutonomyConfigurations", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.BrandProfile", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.PrimitiveCollection<List<string>>("ExampleContent")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.Property<bool>("IsActive")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("Name")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("PersonaDescription")
+                        .IsRequired()
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<string>("StyleGuidelines")
+                        .IsRequired()
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.PrimitiveCollection<List<string>>("ToneDescriptors")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.PrimitiveCollection<List<string>>("Topics")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<string>("VocabularyPreferences")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.ToTable("BrandProfiles", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.CalendarSlot", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("ContentSeriesId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsOverride")
+                        .HasColumnType("boolean");
+
+                    b.Property<DateTimeOffset?>("OverriddenOccurrence")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("ScheduledAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Status")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentId");
+
+                    b.HasIndex("ContentSeriesId");
+
+                    b.HasIndex("Status");
+
+                    b.HasIndex("ScheduledAt", "Platform");
+
+                    b.ToTable("CalendarSlots", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Content", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Body")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<int>("CapturedAutonomyLevel")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("ContentType")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Metadata")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset?>("NextRetryAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid?>("ParentContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset?>("PublishedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset?>("PublishingStartedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int?>("RepurposeSourcePlatform")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("RetryCount")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset?>("ScheduledAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.PrimitiveCollection<int[]>("TargetPlatforms")
+                        .IsRequired()
+                        .HasColumnType("integer[]");
+
+                    b.Property<string>("Title")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<int>("TreeDepth")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ScheduledAt");
+
+                    b.HasIndex("Status");
+
+                    b.HasIndex("Status", "NextRetryAt");
+
+                    b.HasIndex("Status", "ScheduledAt");
+
+                    b.HasIndex("ParentContentId", "RepurposeSourcePlatform", "ContentType")
+                        .IsUnique()
+                        .HasFilter("\"ParentContentId\" IS NOT NULL");
+
+                    b.ToTable("Contents", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.ContentPlatformStatus", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("ErrorMessage")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<string>("IdempotencyKey")
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
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
+                    b.Property<string>("PostUrl")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset?>("PublishedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("RetryCount")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("Status")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IdempotencyKey")
+                        .IsUnique();
+
+                    b.HasIndex("ContentId", "Platform");
+
+                    b.ToTable("ContentPlatformStatuses", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.ContentSeries", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("ContentType")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Description")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset?>("EndsAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsActive")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("Name")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("RecurrenceRule")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<DateTimeOffset>("StartsAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.PrimitiveCollection<int[]>("TargetPlatforms")
+                        .IsRequired()
+                        .HasColumnType("integer[]");
+
+                    b.Property<string>("ThemeTags")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("TimeZoneId")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IsActive");
+
+                    b.ToTable("ContentSeries", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementSnapshot", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int?>("Clicks")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Comments")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid>("ContentPlatformStatusId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset>("FetchedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int?>("Impressions")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Likes")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Shares")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentPlatformStatusId", "FetchedAt")
+                        .IsDescending(false, true);
+
+                    b.ToTable("EngagementSnapshots", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Notification", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsRead")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("boolean")
+                        .HasDefaultValue(false);
+
+                    b.Property<string>("Message")
+                        .IsRequired()
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<int>("Type")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid>("UserId")
+                        .HasColumnType("uuid");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentId");
+
+                    b.HasIndex("UserId", "IsRead", "CreatedAt")
+                        .IsDescending(false, false, true);
+
+                    b.ToTable("Notifications", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.OAuthState", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<byte[]>("EncryptedCodeVerifier")
+                        .HasColumnType("bytea");
+
+                    b.Property<DateTimeOffset>("ExpiresAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("State")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ExpiresAt");
+
+                    b.HasIndex("State")
+                        .IsUnique();
+
+                    b.ToTable("OAuthStates", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Platform", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DisplayName")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<byte[]>("EncryptedAccessToken")
+                        .HasColumnType("bytea");
+
+                    b.Property<byte[]>("EncryptedRefreshToken")
+                        .HasColumnType("bytea");
+
+                    b.PrimitiveCollection<string[]>("GrantedScopes")
+                        .HasColumnType("text[]");
+
+                    b.Property<bool>("IsConnected")
+                        .HasColumnType("boolean");
+
+                    b.Property<DateTimeOffset?>("LastSyncAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("RateLimitState")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("Settings")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset?>("TokenExpiresAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Type")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Type")
+                        .IsUnique();
+
+                    b.ToTable("Platforms", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendItem", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DeduplicationKey")
+                        .HasMaxLength(128)
+                        .HasColumnType("character varying(128)");
+
+                    b.Property<string>("Description")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<DateTimeOffset>("DetectedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("SourceName")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<int>("SourceType")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<Guid?>("TrendSourceId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Url")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("DeduplicationKey")
+                        .IsUnique()
+                        .HasFilter("\"DeduplicationKey\" IS NOT NULL");
+
+                    b.HasIndex("DetectedAt");
+
+                    b.HasIndex("TrendSourceId");
+
+                    b.ToTable("TrendItems", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSource", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ApiUrl")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsEnabled")
+                        .HasColumnType("boolean");
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
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Name", "Type")
+                        .IsUnique();
+
+                    b.ToTable("TrendSources", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestion", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Rationale")
+                        .IsRequired()
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<float>("RelevanceScore")
+                        .HasColumnType("real");
+
+                    b.Property<int>("Status")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("SuggestedContentType")
+                        .HasColumnType("integer");
+
+                    b.PrimitiveCollection<int[]>("SuggestedPlatforms")
+                        .IsRequired()
+                        .HasColumnType("integer[]");
+
+                    b.Property<string>("Topic")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Status");
+
+                    b.ToTable("TrendSuggestions", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestionItem", b =>
+                {
+                    b.Property<Guid>("TrendSuggestionId")
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("TrendItemId")
+                        .HasColumnType("uuid");
+
+                    b.Property<float>("SimilarityScore")
+                        .HasColumnType("real");
+
+                    b.HasKey("TrendSuggestionId", "TrendItemId");
+
+                    b.HasIndex("TrendItemId");
+
+                    b.ToTable("TrendSuggestionItems", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.User", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DisplayName")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("Email")
+                        .IsRequired()
+                        .HasMaxLength(256)
+                        .HasColumnType("character varying(256)");
+
+                    b.Property<string>("Settings")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("TimeZoneId")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Email")
+                        .IsUnique();
+
+                    b.ToTable("Users", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.WorkflowTransitionLog", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ActorId")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<int>("ActorType")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("FromStatus")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("Reason")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset>("Timestamp")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("ToStatus")
+                        .HasColumnType("integer");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Timestamp");
+
+                    b.HasIndex("ContentId", "Timestamp")
+                        .IsDescending(false, true);
+
+                    b.ToTable("WorkflowTransitionLogs", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecution", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecutionLog", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.AgentExecution", null)
+                        .WithMany()
+                        .HasForeignKey("AgentExecutionId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.CalendarSlot", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.ContentSeries", null)
+                        .WithMany()
+                        .HasForeignKey("ContentSeriesId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Content", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ParentContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.ContentPlatformStatus", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementSnapshot", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.ContentPlatformStatus", null)
+                        .WithMany()
+                        .HasForeignKey("ContentPlatformStatusId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Notification", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.User", null)
+                        .WithMany()
+                        .HasForeignKey("UserId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendItem", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendSource", null)
+                        .WithMany()
+                        .HasForeignKey("TrendSourceId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestionItem", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendItem", null)
+                        .WithMany()
+                        .HasForeignKey("TrendItemId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendSuggestion", null)
+                        .WithMany("RelatedTrends")
+                        .HasForeignKey("TrendSuggestionId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.WorkflowTransitionLog", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestion", b =>
+                {
+                    b.Navigation("RelatedTrends");
+                });
+#pragma warning restore 612, 618
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Migrations/20260316193800_InitialCreate.cs b/src/PersonalBrandAssistant.Infrastructure/Migrations/20260316193800_InitialCreate.cs
new file mode 100644
index 0000000..41193f9
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Migrations/20260316193800_InitialCreate.cs
@@ -0,0 +1,717 @@
+﻿using System;
+using System.Collections.Generic;
+using Microsoft.EntityFrameworkCore.Migrations;
+
+#nullable disable
+
+namespace PersonalBrandAssistant.Infrastructure.Migrations
+{
+    /// <inheritdoc />
+    public partial class InitialCreate : Migration
+    {
+        /// <inheritdoc />
+        protected override void Up(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.CreateTable(
+                name: "AuditLogEntries",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    EntityType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
+                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
+                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
+                    OldValue = table.Column<string>(type: "text", nullable: true),
+                    NewValue = table.Column<string>(type: "text", nullable: true),
+                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "AutonomyConfigurations",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    GlobalLevel = table.Column<int>(type: "integer", nullable: false),
+                    ContentTypeOverrides = table.Column<string>(type: "jsonb", nullable: false),
+                    PlatformOverrides = table.Column<string>(type: "jsonb", nullable: false),
+                    ContentTypePlatformOverrides = table.Column<string>(type: "jsonb", nullable: false),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_AutonomyConfigurations", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "BrandProfiles",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
+                    ToneDescriptors = table.Column<List<string>>(type: "text[]", nullable: false),
+                    StyleGuidelines = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
+                    VocabularyPreferences = table.Column<string>(type: "jsonb", nullable: false),
+                    Topics = table.Column<List<string>>(type: "text[]", nullable: false),
+                    PersonaDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
+                    ExampleContent = table.Column<List<string>>(type: "text[]", nullable: false),
+                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
+                    Version = table.Column<long>(type: "bigint", nullable: false),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_BrandProfiles", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "Contents",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    ContentType = table.Column<int>(type: "integer", nullable: false),
+                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
+                    Body = table.Column<string>(type: "text", nullable: false),
+                    Status = table.Column<int>(type: "integer", nullable: false),
+                    Metadata = table.Column<string>(type: "jsonb", nullable: false),
+                    ParentContentId = table.Column<Guid>(type: "uuid", nullable: true),
+                    TargetPlatforms = table.Column<int[]>(type: "integer[]", nullable: false),
+                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    CapturedAutonomyLevel = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    PublishingStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    TreeDepth = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    RepurposeSourcePlatform = table.Column<int>(type: "integer", nullable: true),
+                    Version = table.Column<long>(type: "bigint", nullable: false),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_Contents", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_Contents_Contents_ParentContentId",
+                        column: x => x.ParentContentId,
+                        principalTable: "Contents",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.SetNull);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "ContentSeries",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
+                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
+                    RecurrenceRule = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
+                    TargetPlatforms = table.Column<int[]>(type: "integer[]", nullable: false),
+                    ContentType = table.Column<int>(type: "integer", nullable: false),
+                    ThemeTags = table.Column<string>(type: "jsonb", nullable: false),
+                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
+                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
+                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_ContentSeries", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "OAuthStates",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    State = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
+                    Platform = table.Column<int>(type: "integer", nullable: false),
+                    EncryptedCodeVerifier = table.Column<byte[]>(type: "bytea", nullable: true),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_OAuthStates", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "Platforms",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Type = table.Column<int>(type: "integer", nullable: false),
+                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
+                    IsConnected = table.Column<bool>(type: "boolean", nullable: false),
+                    EncryptedAccessToken = table.Column<byte[]>(type: "bytea", nullable: true),
+                    EncryptedRefreshToken = table.Column<byte[]>(type: "bytea", nullable: true),
+                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    GrantedScopes = table.Column<string[]>(type: "text[]", nullable: true),
+                    RateLimitState = table.Column<string>(type: "jsonb", nullable: false),
+                    LastSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    Settings = table.Column<string>(type: "jsonb", nullable: false),
+                    Version = table.Column<long>(type: "bigint", nullable: false),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_Platforms", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "TrendSources",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
+                    Type = table.Column<int>(type: "integer", nullable: false),
+                    ApiUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
+                    PollIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
+                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_TrendSources", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "TrendSuggestions",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Topic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
+                    Rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
+                    RelevanceScore = table.Column<float>(type: "real", nullable: false),
+                    SuggestedContentType = table.Column<int>(type: "integer", nullable: false),
+                    SuggestedPlatforms = table.Column<int[]>(type: "integer[]", nullable: false),
+                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_TrendSuggestions", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "Users",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
+                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
+                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
+                    Settings = table.Column<string>(type: "jsonb", nullable: false),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_Users", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "AgentExecutions",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
+                    AgentType = table.Column<int>(type: "integer", nullable: false),
+                    Status = table.Column<int>(type: "integer", nullable: false),
+                    ModelUsed = table.Column<int>(type: "integer", nullable: false),
+                    ModelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
+                    InputTokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    OutputTokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    CacheReadTokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    CacheCreationTokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    Cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
+                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    Duration = table.Column<TimeSpan>(type: "interval", nullable: true),
+                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
+                    OutputSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_AgentExecutions", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_AgentExecutions_Contents_ContentId",
+                        column: x => x.ContentId,
+                        principalTable: "Contents",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.SetNull);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "ContentPlatformStatuses",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
+                    Platform = table.Column<int>(type: "integer", nullable: false),
+                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    PlatformPostId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
+                    PostUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
+                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
+                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
+                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    Version = table.Column<long>(type: "bigint", nullable: false),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_ContentPlatformStatuses", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_ContentPlatformStatuses_Contents_ContentId",
+                        column: x => x.ContentId,
+                        principalTable: "Contents",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.Cascade);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "WorkflowTransitionLogs",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
+                    FromStatus = table.Column<int>(type: "integer", nullable: false),
+                    ToStatus = table.Column<int>(type: "integer", nullable: false),
+                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
+                    ActorType = table.Column<int>(type: "integer", nullable: false),
+                    ActorId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
+                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_WorkflowTransitionLogs", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_WorkflowTransitionLogs_Contents_ContentId",
+                        column: x => x.ContentId,
+                        principalTable: "Contents",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.Cascade);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "CalendarSlots",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    Platform = table.Column<int>(type: "integer", nullable: false),
+                    ContentSeriesId = table.Column<Guid>(type: "uuid", nullable: true),
+                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
+                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    IsOverride = table.Column<bool>(type: "boolean", nullable: false),
+                    OverriddenOccurrence = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_CalendarSlots", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_CalendarSlots_ContentSeries_ContentSeriesId",
+                        column: x => x.ContentSeriesId,
+                        principalTable: "ContentSeries",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.SetNull);
+                    table.ForeignKey(
+                        name: "FK_CalendarSlots_Contents_ContentId",
+                        column: x => x.ContentId,
+                        principalTable: "Contents",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.SetNull);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "TrendItems",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
+                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
+                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
+                    SourceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
+                    SourceType = table.Column<int>(type: "integer", nullable: false),
+                    TrendSourceId = table.Column<Guid>(type: "uuid", nullable: true),
+                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    DeduplicationKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_TrendItems", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_TrendItems_TrendSources_TrendSourceId",
+                        column: x => x.TrendSourceId,
+                        principalTable: "TrendSources",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.SetNull);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "Notifications",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
+                    Type = table.Column<int>(type: "integer", nullable: false),
+                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
+                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
+                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
+                    IsRead = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_Notifications", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_Notifications_Contents_ContentId",
+                        column: x => x.ContentId,
+                        principalTable: "Contents",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.SetNull);
+                    table.ForeignKey(
+                        name: "FK_Notifications_Users_UserId",
+                        column: x => x.UserId,
+                        principalTable: "Users",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.Cascade);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "AgentExecutionLogs",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    AgentExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
+                    StepNumber = table.Column<int>(type: "integer", nullable: false),
+                    StepType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
+                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
+                    TokensUsed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
+                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_AgentExecutionLogs", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_AgentExecutionLogs_AgentExecutions_AgentExecutionId",
+                        column: x => x.AgentExecutionId,
+                        principalTable: "AgentExecutions",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.Cascade);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "EngagementSnapshots",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    ContentPlatformStatusId = table.Column<Guid>(type: "uuid", nullable: false),
+                    Likes = table.Column<int>(type: "integer", nullable: false),
+                    Comments = table.Column<int>(type: "integer", nullable: false),
+                    Shares = table.Column<int>(type: "integer", nullable: false),
+                    Impressions = table.Column<int>(type: "integer", nullable: true),
+                    Clicks = table.Column<int>(type: "integer", nullable: true),
+                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_EngagementSnapshots", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_EngagementSnapshots_ContentPlatformStatuses_ContentPlatform~",
+                        column: x => x.ContentPlatformStatusId,
+                        principalTable: "ContentPlatformStatuses",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.Cascade);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "TrendSuggestionItems",
+                columns: table => new
+                {
+                    TrendSuggestionId = table.Column<Guid>(type: "uuid", nullable: false),
+                    TrendItemId = table.Column<Guid>(type: "uuid", nullable: false),
+                    SimilarityScore = table.Column<float>(type: "real", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_TrendSuggestionItems", x => new { x.TrendSuggestionId, x.TrendItemId });
+                    table.ForeignKey(
+                        name: "FK_TrendSuggestionItems_TrendItems_TrendItemId",
+                        column: x => x.TrendItemId,
+                        principalTable: "TrendItems",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.Cascade);
+                    table.ForeignKey(
+                        name: "FK_TrendSuggestionItems_TrendSuggestions_TrendSuggestionId",
+                        column: x => x.TrendSuggestionId,
+                        principalTable: "TrendSuggestions",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.Cascade);
+                });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_AgentExecutionLogs_AgentExecutionId",
+                table: "AgentExecutionLogs",
+                column: "AgentExecutionId");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_AgentExecutions_ContentId",
+                table: "AgentExecutions",
+                column: "ContentId");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_AgentExecutions_Status_AgentType",
+                table: "AgentExecutions",
+                columns: new[] { "Status", "AgentType" });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_AuditLogEntries_Timestamp",
+                table: "AuditLogEntries",
+                column: "Timestamp");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_CalendarSlots_ContentId",
+                table: "CalendarSlots",
+                column: "ContentId");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_CalendarSlots_ContentSeriesId",
+                table: "CalendarSlots",
+                column: "ContentSeriesId");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_CalendarSlots_ScheduledAt_Platform",
+                table: "CalendarSlots",
+                columns: new[] { "ScheduledAt", "Platform" });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_CalendarSlots_Status",
+                table: "CalendarSlots",
+                column: "Status");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_ContentPlatformStatuses_ContentId_Platform",
+                table: "ContentPlatformStatuses",
+                columns: new[] { "ContentId", "Platform" });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_ContentPlatformStatuses_IdempotencyKey",
+                table: "ContentPlatformStatuses",
+                column: "IdempotencyKey",
+                unique: true);
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Contents_ParentContentId_RepurposeSourcePlatform_ContentType",
+                table: "Contents",
+                columns: new[] { "ParentContentId", "RepurposeSourcePlatform", "ContentType" },
+                unique: true,
+                filter: "\"ParentContentId\" IS NOT NULL");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Contents_ScheduledAt",
+                table: "Contents",
+                column: "ScheduledAt");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Contents_Status",
+                table: "Contents",
+                column: "Status");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Contents_Status_NextRetryAt",
+                table: "Contents",
+                columns: new[] { "Status", "NextRetryAt" });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Contents_Status_ScheduledAt",
+                table: "Contents",
+                columns: new[] { "Status", "ScheduledAt" });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_ContentSeries_IsActive",
+                table: "ContentSeries",
+                column: "IsActive");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_EngagementSnapshots_ContentPlatformStatusId_FetchedAt",
+                table: "EngagementSnapshots",
+                columns: new[] { "ContentPlatformStatusId", "FetchedAt" },
+                descending: new[] { false, true });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Notifications_ContentId",
+                table: "Notifications",
+                column: "ContentId");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Notifications_UserId_IsRead_CreatedAt",
+                table: "Notifications",
+                columns: new[] { "UserId", "IsRead", "CreatedAt" },
+                descending: new[] { false, false, true });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_OAuthStates_ExpiresAt",
+                table: "OAuthStates",
+                column: "ExpiresAt");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_OAuthStates_State",
+                table: "OAuthStates",
+                column: "State",
+                unique: true);
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Platforms_Type",
+                table: "Platforms",
+                column: "Type",
+                unique: true);
+
+            migrationBuilder.CreateIndex(
+                name: "IX_TrendItems_DeduplicationKey",
+                table: "TrendItems",
+                column: "DeduplicationKey",
+                unique: true,
+                filter: "\"DeduplicationKey\" IS NOT NULL");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_TrendItems_DetectedAt",
+                table: "TrendItems",
+                column: "DetectedAt");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_TrendItems_TrendSourceId",
+                table: "TrendItems",
+                column: "TrendSourceId");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_TrendSources_Name_Type",
+                table: "TrendSources",
+                columns: new[] { "Name", "Type" },
+                unique: true);
+
+            migrationBuilder.CreateIndex(
+                name: "IX_TrendSuggestionItems_TrendItemId",
+                table: "TrendSuggestionItems",
+                column: "TrendItemId");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_TrendSuggestions_Status",
+                table: "TrendSuggestions",
+                column: "Status");
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Users_Email",
+                table: "Users",
+                column: "Email",
+                unique: true);
+
+            migrationBuilder.CreateIndex(
+                name: "IX_WorkflowTransitionLogs_ContentId_Timestamp",
+                table: "WorkflowTransitionLogs",
+                columns: new[] { "ContentId", "Timestamp" },
+                descending: new[] { false, true });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_WorkflowTransitionLogs_Timestamp",
+                table: "WorkflowTransitionLogs",
+                column: "Timestamp");
+        }
+
+        /// <inheritdoc />
+        protected override void Down(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.DropTable(
+                name: "AgentExecutionLogs");
+
+            migrationBuilder.DropTable(
+                name: "AuditLogEntries");
+
+            migrationBuilder.DropTable(
+                name: "AutonomyConfigurations");
+
+            migrationBuilder.DropTable(
+                name: "BrandProfiles");
+
+            migrationBuilder.DropTable(
+                name: "CalendarSlots");
+
+            migrationBuilder.DropTable(
+                name: "EngagementSnapshots");
+
+            migrationBuilder.DropTable(
+                name: "Notifications");
+
+            migrationBuilder.DropTable(
+                name: "OAuthStates");
+
+            migrationBuilder.DropTable(
+                name: "Platforms");
+
+            migrationBuilder.DropTable(
+                name: "TrendSuggestionItems");
+
+            migrationBuilder.DropTable(
+                name: "WorkflowTransitionLogs");
+
+            migrationBuilder.DropTable(
+                name: "AgentExecutions");
+
+            migrationBuilder.DropTable(
+                name: "ContentSeries");
+
+            migrationBuilder.DropTable(
+                name: "ContentPlatformStatuses");
+
+            migrationBuilder.DropTable(
+                name: "Users");
+
+            migrationBuilder.DropTable(
+                name: "TrendItems");
+
+            migrationBuilder.DropTable(
+                name: "TrendSuggestions");
+
+            migrationBuilder.DropTable(
+                name: "Contents");
+
+            migrationBuilder.DropTable(
+                name: "TrendSources");
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Migrations/20260317014636_AddInterestKeywordsAndSavedItems.Designer.cs b/src/PersonalBrandAssistant.Infrastructure/Migrations/20260317014636_AddInterestKeywordsAndSavedItems.Designer.cs
new file mode 100644
index 0000000..994fd2f
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Migrations/20260317014636_AddInterestKeywordsAndSavedItems.Designer.cs
@@ -0,0 +1,1191 @@
+﻿// <auto-generated />
+using System;
+using System.Collections.Generic;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Infrastructure;
+using Microsoft.EntityFrameworkCore.Migrations;
+using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
+using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
+using PersonalBrandAssistant.Infrastructure.Data;
+
+#nullable disable
+
+namespace PersonalBrandAssistant.Infrastructure.Migrations
+{
+    [DbContext(typeof(ApplicationDbContext))]
+    [Migration("20260317014636_AddInterestKeywordsAndSavedItems")]
+    partial class AddInterestKeywordsAndSavedItems
+    {
+        /// <inheritdoc />
+        protected override void BuildTargetModel(ModelBuilder modelBuilder)
+        {
+#pragma warning disable 612, 618
+            modelBuilder
+                .HasAnnotation("ProductVersion", "10.0.5")
+                .HasAnnotation("Relational:MaxIdentifierLength", 63);
+
+            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecution", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("AgentType")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("CacheCreationTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("CacheReadTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset?>("CompletedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid?>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<decimal>("Cost")
+                        .HasPrecision(18, 6)
+                        .HasColumnType("numeric(18,6)");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<TimeSpan?>("Duration")
+                        .HasColumnType("interval");
+
+                    b.Property<string>("Error")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<int>("InputTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<string>("ModelId")
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<int>("ModelUsed")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("OutputSummary")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<int>("OutputTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("StartedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentId");
+
+                    b.HasIndex("Status", "AgentType");
+
+                    b.ToTable("AgentExecutions", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecutionLog", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("AgentExecutionId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Content")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<int>("StepNumber")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("StepType")
+                        .IsRequired()
+                        .HasMaxLength(50)
+                        .HasColumnType("character varying(50)");
+
+                    b.Property<DateTimeOffset>("Timestamp")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("TokensUsed")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("AgentExecutionId");
+
+                    b.ToTable("AgentExecutionLogs", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AuditLogEntry", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Action")
+                        .IsRequired()
+                        .HasMaxLength(50)
+                        .HasColumnType("character varying(50)");
+
+                    b.Property<string>("Details")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<Guid>("EntityId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("EntityType")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("NewValue")
+                        .HasColumnType("text");
+
+                    b.Property<string>("OldValue")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset>("Timestamp")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Timestamp");
+
+                    b.ToTable("AuditLogEntries", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AutonomyConfiguration", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ContentTypeOverrides")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("ContentTypePlatformOverrides")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("GlobalLevel")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("PlatformOverrides")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.ToTable("AutonomyConfigurations", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.BrandProfile", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.PrimitiveCollection<List<string>>("ExampleContent")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.Property<bool>("IsActive")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("Name")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("PersonaDescription")
+                        .IsRequired()
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<string>("StyleGuidelines")
+                        .IsRequired()
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.PrimitiveCollection<List<string>>("ToneDescriptors")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.PrimitiveCollection<List<string>>("Topics")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<string>("VocabularyPreferences")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.ToTable("BrandProfiles", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.CalendarSlot", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("ContentSeriesId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsOverride")
+                        .HasColumnType("boolean");
+
+                    b.Property<DateTimeOffset?>("OverriddenOccurrence")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("ScheduledAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Status")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentId");
+
+                    b.HasIndex("ContentSeriesId");
+
+                    b.HasIndex("Status");
+
+                    b.HasIndex("ScheduledAt", "Platform");
+
+                    b.ToTable("CalendarSlots", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Content", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Body")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<int>("CapturedAutonomyLevel")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("ContentType")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Metadata")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset?>("NextRetryAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid?>("ParentContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset?>("PublishedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset?>("PublishingStartedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int?>("RepurposeSourcePlatform")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("RetryCount")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset?>("ScheduledAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.PrimitiveCollection<int[]>("TargetPlatforms")
+                        .IsRequired()
+                        .HasColumnType("integer[]");
+
+                    b.Property<string>("Title")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<int>("TreeDepth")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ScheduledAt");
+
+                    b.HasIndex("Status");
+
+                    b.HasIndex("Status", "NextRetryAt");
+
+                    b.HasIndex("Status", "ScheduledAt");
+
+                    b.HasIndex("ParentContentId", "RepurposeSourcePlatform", "ContentType")
+                        .IsUnique()
+                        .HasFilter("\"ParentContentId\" IS NOT NULL");
+
+                    b.ToTable("Contents", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.ContentPlatformStatus", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("ErrorMessage")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<string>("IdempotencyKey")
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
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
+                    b.Property<string>("PostUrl")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset?>("PublishedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("RetryCount")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("Status")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IdempotencyKey")
+                        .IsUnique();
+
+                    b.HasIndex("ContentId", "Platform");
+
+                    b.ToTable("ContentPlatformStatuses", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.ContentSeries", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("ContentType")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Description")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset?>("EndsAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsActive")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("Name")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("RecurrenceRule")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<DateTimeOffset>("StartsAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.PrimitiveCollection<int[]>("TargetPlatforms")
+                        .IsRequired()
+                        .HasColumnType("integer[]");
+
+                    b.Property<string>("ThemeTags")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("TimeZoneId")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IsActive");
+
+                    b.ToTable("ContentSeries", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementSnapshot", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int?>("Clicks")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Comments")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid>("ContentPlatformStatusId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset>("FetchedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int?>("Impressions")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Likes")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Shares")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentPlatformStatusId", "FetchedAt")
+                        .IsDescending(false, true);
+
+                    b.ToTable("EngagementSnapshots", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.InterestKeyword", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Keyword")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<int>("MatchCount")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<double>("Weight")
+                        .HasColumnType("double precision");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Keyword")
+                        .IsUnique();
+
+                    b.ToTable("InterestKeywords", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Notification", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsRead")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("boolean")
+                        .HasDefaultValue(false);
+
+                    b.Property<string>("Message")
+                        .IsRequired()
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<int>("Type")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid>("UserId")
+                        .HasColumnType("uuid");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentId");
+
+                    b.HasIndex("UserId", "IsRead", "CreatedAt")
+                        .IsDescending(false, false, true);
+
+                    b.ToTable("Notifications", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.OAuthState", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<byte[]>("EncryptedCodeVerifier")
+                        .HasColumnType("bytea");
+
+                    b.Property<DateTimeOffset>("ExpiresAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("State")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ExpiresAt");
+
+                    b.HasIndex("State")
+                        .IsUnique();
+
+                    b.ToTable("OAuthStates", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Platform", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DisplayName")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<byte[]>("EncryptedAccessToken")
+                        .HasColumnType("bytea");
+
+                    b.Property<byte[]>("EncryptedRefreshToken")
+                        .HasColumnType("bytea");
+
+                    b.PrimitiveCollection<string[]>("GrantedScopes")
+                        .HasColumnType("text[]");
+
+                    b.Property<bool>("IsConnected")
+                        .HasColumnType("boolean");
+
+                    b.Property<DateTimeOffset?>("LastSyncAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("RateLimitState")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("Settings")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset?>("TokenExpiresAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Type")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Type")
+                        .IsUnique();
+
+                    b.ToTable("Platforms", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.SavedTrendItem", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Notes")
+                        .HasMaxLength(1000)
+                        .HasColumnType("character varying(1000)");
+
+                    b.Property<DateTimeOffset>("SavedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid>("TrendItemId")
+                        .HasColumnType("uuid");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("TrendItemId")
+                        .IsUnique();
+
+                    b.ToTable("SavedTrendItems", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendItem", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DeduplicationKey")
+                        .HasMaxLength(128)
+                        .HasColumnType("character varying(128)");
+
+                    b.Property<string>("Description")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<DateTimeOffset>("DetectedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("SourceName")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<int>("SourceType")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<Guid?>("TrendSourceId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Url")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("DeduplicationKey")
+                        .IsUnique()
+                        .HasFilter("\"DeduplicationKey\" IS NOT NULL");
+
+                    b.HasIndex("DetectedAt");
+
+                    b.HasIndex("TrendSourceId");
+
+                    b.ToTable("TrendItems", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSource", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ApiUrl")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsEnabled")
+                        .HasColumnType("boolean");
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
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Name", "Type")
+                        .IsUnique();
+
+                    b.ToTable("TrendSources", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestion", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Rationale")
+                        .IsRequired()
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<float>("RelevanceScore")
+                        .HasColumnType("real");
+
+                    b.Property<int>("Status")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("SuggestedContentType")
+                        .HasColumnType("integer");
+
+                    b.PrimitiveCollection<int[]>("SuggestedPlatforms")
+                        .IsRequired()
+                        .HasColumnType("integer[]");
+
+                    b.Property<string>("Topic")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Status");
+
+                    b.ToTable("TrendSuggestions", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestionItem", b =>
+                {
+                    b.Property<Guid>("TrendSuggestionId")
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("TrendItemId")
+                        .HasColumnType("uuid");
+
+                    b.Property<float>("SimilarityScore")
+                        .HasColumnType("real");
+
+                    b.HasKey("TrendSuggestionId", "TrendItemId");
+
+                    b.HasIndex("TrendItemId");
+
+                    b.ToTable("TrendSuggestionItems", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.User", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DisplayName")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("Email")
+                        .IsRequired()
+                        .HasMaxLength(256)
+                        .HasColumnType("character varying(256)");
+
+                    b.Property<string>("Settings")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("TimeZoneId")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Email")
+                        .IsUnique();
+
+                    b.ToTable("Users", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.WorkflowTransitionLog", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ActorId")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<int>("ActorType")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("FromStatus")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("Reason")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset>("Timestamp")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("ToStatus")
+                        .HasColumnType("integer");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Timestamp");
+
+                    b.HasIndex("ContentId", "Timestamp")
+                        .IsDescending(false, true);
+
+                    b.ToTable("WorkflowTransitionLogs", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecution", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecutionLog", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.AgentExecution", null)
+                        .WithMany()
+                        .HasForeignKey("AgentExecutionId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.CalendarSlot", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.ContentSeries", null)
+                        .WithMany()
+                        .HasForeignKey("ContentSeriesId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Content", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ParentContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.ContentPlatformStatus", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementSnapshot", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.ContentPlatformStatus", null)
+                        .WithMany()
+                        .HasForeignKey("ContentPlatformStatusId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Notification", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.User", null)
+                        .WithMany()
+                        .HasForeignKey("UserId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.SavedTrendItem", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendItem", "TrendItem")
+                        .WithMany()
+                        .HasForeignKey("TrendItemId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.Navigation("TrendItem");
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendItem", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendSource", null)
+                        .WithMany()
+                        .HasForeignKey("TrendSourceId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestionItem", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendItem", null)
+                        .WithMany()
+                        .HasForeignKey("TrendItemId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendSuggestion", null)
+                        .WithMany("RelatedTrends")
+                        .HasForeignKey("TrendSuggestionId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.WorkflowTransitionLog", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestion", b =>
+                {
+                    b.Navigation("RelatedTrends");
+                });
+#pragma warning restore 612, 618
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Migrations/20260317014636_AddInterestKeywordsAndSavedItems.cs b/src/PersonalBrandAssistant.Infrastructure/Migrations/20260317014636_AddInterestKeywordsAndSavedItems.cs
new file mode 100644
index 0000000..e2afe33
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Migrations/20260317014636_AddInterestKeywordsAndSavedItems.cs
@@ -0,0 +1,74 @@
+﻿using System;
+using Microsoft.EntityFrameworkCore.Migrations;
+
+#nullable disable
+
+namespace PersonalBrandAssistant.Infrastructure.Migrations
+{
+    /// <inheritdoc />
+    public partial class AddInterestKeywordsAndSavedItems : Migration
+    {
+        /// <inheritdoc />
+        protected override void Up(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.CreateTable(
+                name: "InterestKeywords",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Keyword = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
+                    Weight = table.Column<double>(type: "double precision", nullable: false),
+                    MatchCount = table.Column<int>(type: "integer", nullable: false),
+                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_InterestKeywords", x => x.Id);
+                });
+
+            migrationBuilder.CreateTable(
+                name: "SavedTrendItems",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    TrendItemId = table.Column<Guid>(type: "uuid", nullable: false),
+                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_SavedTrendItems", x => x.Id);
+                    table.ForeignKey(
+                        name: "FK_SavedTrendItems_TrendItems_TrendItemId",
+                        column: x => x.TrendItemId,
+                        principalTable: "TrendItems",
+                        principalColumn: "Id",
+                        onDelete: ReferentialAction.Cascade);
+                });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_InterestKeywords_Keyword",
+                table: "InterestKeywords",
+                column: "Keyword",
+                unique: true);
+
+            migrationBuilder.CreateIndex(
+                name: "IX_SavedTrendItems_TrendItemId",
+                table: "SavedTrendItems",
+                column: "TrendItemId",
+                unique: true);
+        }
+
+        /// <inheritdoc />
+        protected override void Down(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.DropTable(
+                name: "InterestKeywords");
+
+            migrationBuilder.DropTable(
+                name: "SavedTrendItems");
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs b/src/PersonalBrandAssistant.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs
new file mode 100644
index 0000000..6a33576
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs
@@ -0,0 +1,1615 @@
+﻿// <auto-generated />
+using System;
+using System.Collections.Generic;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Infrastructure;
+using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
+using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
+using PersonalBrandAssistant.Infrastructure.Data;
+
+#nullable disable
+
+namespace PersonalBrandAssistant.Infrastructure.Migrations
+{
+    [DbContext(typeof(ApplicationDbContext))]
+    partial class ApplicationDbContextModelSnapshot : ModelSnapshot
+    {
+        protected override void BuildModel(ModelBuilder modelBuilder)
+        {
+#pragma warning disable 612, 618
+            modelBuilder
+                .HasAnnotation("ProductVersion", "10.0.5")
+                .HasAnnotation("Relational:MaxIdentifierLength", 63);
+
+            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecution", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("AgentType")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("CacheCreationTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("CacheReadTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset?>("CompletedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid?>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<decimal>("Cost")
+                        .HasPrecision(18, 6)
+                        .HasColumnType("numeric(18,6)");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<TimeSpan?>("Duration")
+                        .HasColumnType("interval");
+
+                    b.Property<string>("Error")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<int>("InputTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<string>("ModelId")
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<int>("ModelUsed")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("OutputSummary")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<int>("OutputTokens")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("StartedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentId");
+
+                    b.HasIndex("Status", "AgentType");
+
+                    b.ToTable("AgentExecutions", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecutionLog", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("AgentExecutionId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Content")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<int>("StepNumber")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("StepType")
+                        .IsRequired()
+                        .HasMaxLength(50)
+                        .HasColumnType("character varying(50)");
+
+                    b.Property<DateTimeOffset>("Timestamp")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("TokensUsed")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("AgentExecutionId");
+
+                    b.ToTable("AgentExecutionLogs", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AuditLogEntry", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Action")
+                        .IsRequired()
+                        .HasMaxLength(50)
+                        .HasColumnType("character varying(50)");
+
+                    b.Property<string>("Details")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<Guid>("EntityId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("EntityType")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("NewValue")
+                        .HasColumnType("text");
+
+                    b.Property<string>("OldValue")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset>("Timestamp")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Timestamp");
+
+                    b.ToTable("AuditLogEntries", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AutomationRun", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset?>("CompletedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("DurationMs")
+                        .HasColumnType("bigint");
+
+                    b.Property<string>("ErrorDetails")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<string>("ImageFileId")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<string>("ImagePrompt")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<int>("PlatformVersionCount")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid?>("PrimaryContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("SelectedSuggestionId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("SelectionReasoning")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("TriggeredAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Status");
+
+                    b.HasIndex("TriggeredAt");
+
+                    b.ToTable("AutomationRuns", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AutonomyConfiguration", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ContentTypeOverrides")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("ContentTypePlatformOverrides")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("GlobalLevel")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("PlatformOverrides")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.ToTable("AutonomyConfigurations", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.BrandProfile", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.PrimitiveCollection<List<string>>("ExampleContent")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.Property<bool>("IsActive")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("Name")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("PersonaDescription")
+                        .IsRequired()
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<string>("StyleGuidelines")
+                        .IsRequired()
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.PrimitiveCollection<List<string>>("ToneDescriptors")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.PrimitiveCollection<List<string>>("Topics")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<string>("VocabularyPreferences")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.ToTable("BrandProfiles", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.CalendarSlot", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("ContentSeriesId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsOverride")
+                        .HasColumnType("boolean");
+
+                    b.Property<DateTimeOffset?>("OverriddenOccurrence")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("ScheduledAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Status")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentId");
+
+                    b.HasIndex("ContentSeriesId");
+
+                    b.HasIndex("Status");
+
+                    b.HasIndex("ScheduledAt", "Platform");
+
+                    b.ToTable("CalendarSlots", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Content", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Body")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<int>("CapturedAutonomyLevel")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("ContentType")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("ImageFileId")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<bool>("ImageRequired")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("boolean")
+                        .HasDefaultValue(false);
+
+                    b.Property<string>("Metadata")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset?>("NextRetryAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid?>("ParentContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset?>("PublishedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset?>("PublishingStartedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int?>("RepurposeSourcePlatform")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("RetryCount")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset?>("ScheduledAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.PrimitiveCollection<int[]>("TargetPlatforms")
+                        .IsRequired()
+                        .HasColumnType("integer[]");
+
+                    b.Property<string>("Title")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<int>("TreeDepth")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ScheduledAt");
+
+                    b.HasIndex("Status");
+
+                    b.HasIndex("Status", "NextRetryAt");
+
+                    b.HasIndex("Status", "ScheduledAt");
+
+                    b.HasIndex("ParentContentId", "RepurposeSourcePlatform", "ContentType")
+                        .IsUnique()
+                        .HasFilter("\"ParentContentId\" IS NOT NULL");
+
+                    b.ToTable("Contents", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.ContentPlatformStatus", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("ErrorMessage")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<string>("IdempotencyKey")
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
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
+                    b.Property<string>("PostUrl")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset?>("PublishedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("RetryCount")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("Status")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IdempotencyKey")
+                        .IsUnique();
+
+                    b.HasIndex("ContentId", "Platform");
+
+                    b.HasIndex("PublishedAt", "Platform")
+                        .HasFilter("\"PublishedAt\" IS NOT NULL");
+
+                    b.ToTable("ContentPlatformStatuses", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.ContentSeries", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("ContentType")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Description")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset?>("EndsAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsActive")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("Name")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("RecurrenceRule")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<DateTimeOffset>("StartsAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.PrimitiveCollection<int[]>("TargetPlatforms")
+                        .IsRequired()
+                        .HasColumnType("integer[]");
+
+                    b.Property<string>("ThemeTags")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("TimeZoneId")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IsActive");
+
+                    b.ToTable("ContentSeries", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementAction", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("ActionType")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid>("EngagementExecutionId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ErrorMessage")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<string>("GeneratedContent")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset>("PerformedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("PlatformPostId")
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<bool>("Succeeded")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("TargetUrl")
+                        .IsRequired()
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("EngagementExecutionId");
+
+                    b.ToTable("EngagementActions", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementExecution", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("ActionsAttempted")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("ActionsSucceeded")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid>("EngagementTaskId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ErrorMessage")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset>("ExecutedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("EngagementTaskId");
+
+                    b.HasIndex("ExecutedAt");
+
+                    b.ToTable("EngagementExecutions", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementSnapshot", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<int?>("Clicks")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Comments")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid>("ContentPlatformStatusId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset>("FetchedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int?>("Impressions")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Likes")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Shares")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentPlatformStatusId", "FetchedAt")
+                        .IsDescending(false, true);
+
+                    b.HasIndex("FetchedAt", "ContentPlatformStatusId");
+
+                    b.ToTable("EngagementSnapshots", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementTask", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<bool>("AutoRespond")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("boolean")
+                        .HasDefaultValue(false);
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("CronExpression")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<bool>("IsEnabled")
+                        .HasColumnType("boolean");
+
+                    b.Property<DateTimeOffset?>("LastExecutedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("MaxActionsPerExecution")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(3);
+
+                    b.Property<DateTimeOffset?>("NextExecutionAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("SchedulingMode")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(1);
+
+                    b.Property<bool>("SkippedLastExecution")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("boolean")
+                        .HasDefaultValue(false);
+
+                    b.Property<string>("TargetCriteria")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<int>("TaskType")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Platform", "IsEnabled", "AutoRespond", "NextExecutionAt");
+
+                    b.ToTable("EngagementTasks", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.InterestKeyword", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Keyword")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<int>("MatchCount")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<double>("Weight")
+                        .HasColumnType("double precision");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Keyword")
+                        .IsUnique();
+
+                    b.ToTable("InterestKeywords", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Notification", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid?>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("IsRead")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("boolean")
+                        .HasDefaultValue(false);
+
+                    b.Property<string>("Message")
+                        .IsRequired()
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<int>("Type")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid>("UserId")
+                        .HasColumnType("uuid");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ContentId");
+
+                    b.HasIndex("UserId", "IsRead", "CreatedAt")
+                        .IsDescending(false, false, true);
+
+                    b.ToTable("Notifications", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.OAuthState", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<byte[]>("EncryptedCodeVerifier")
+                        .HasColumnType("bytea");
+
+                    b.Property<DateTimeOffset>("ExpiresAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("State")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("ExpiresAt");
+
+                    b.HasIndex("State")
+                        .IsUnique();
+
+                    b.ToTable("OAuthStates", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.OpportunityAction", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("PostUrl")
+                        .IsRequired()
+                        .HasMaxLength(2048)
+                        .HasColumnType("character varying(2048)");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Platform", "PostUrl")
+                        .IsUnique();
+
+                    b.ToTable("OpportunityActions", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Platform", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DisplayName")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<byte[]>("EncryptedAccessToken")
+                        .HasColumnType("bytea");
+
+                    b.Property<byte[]>("EncryptedRefreshToken")
+                        .HasColumnType("bytea");
+
+                    b.PrimitiveCollection<string[]>("GrantedScopes")
+                        .HasColumnType("text[]");
+
+                    b.Property<bool>("IsConnected")
+                        .HasColumnType("boolean");
+
+                    b.Property<DateTimeOffset?>("LastSyncAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("RateLimitState")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("Settings")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<DateTimeOffset?>("TokenExpiresAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("Type")
+                        .HasColumnType("integer");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<long>("Version")
+                        .HasColumnType("bigint");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Type")
+                        .IsUnique();
+
+                    b.ToTable("Platforms", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.SavedTrendItem", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Notes")
+                        .HasMaxLength(1000)
+                        .HasColumnType("character varying(1000)");
+
+                    b.Property<DateTimeOffset>("SavedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid>("TrendItemId")
+                        .HasColumnType("uuid");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("TrendItemId")
+                        .IsUnique();
+
+                    b.ToTable("SavedTrendItems", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.SocialInboxItem", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("AuthorName")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("AuthorProfileUrl")
+                        .IsRequired()
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<string>("Content")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DraftReply")
+                        .HasColumnType("text");
+
+                    b.Property<bool>("IsRead")
+                        .HasColumnType("boolean");
+
+                    b.Property<int>("ItemType")
+                        .HasColumnType("integer");
+
+                    b.Property<int>("Platform")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("PlatformItemId")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<DateTimeOffset>("ReceivedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<bool>("ReplySent")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("SourceUrl")
+                        .IsRequired()
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IsRead", "ReceivedAt");
+
+                    b.HasIndex("Platform", "PlatformItemId")
+                        .IsUnique();
+
+                    b.ToTable("SocialInboxItems", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendItem", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("Category")
+                        .HasMaxLength(50)
+                        .HasColumnType("character varying(50)");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DeduplicationKey")
+                        .HasMaxLength(128)
+                        .HasColumnType("character varying(128)");
+
+                    b.Property<string>("Description")
+                        .HasMaxLength(4000)
+                        .HasColumnType("character varying(4000)");
+
+                    b.Property<DateTimeOffset>("DetectedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("SourceName")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<int>("SourceType")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("Summary")
+                        .HasColumnType("text");
+
+                    b.Property<string>("ThumbnailUrl")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<Guid?>("TrendSourceId")
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Url")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("DeduplicationKey")
+                        .IsUnique()
+                        .HasFilter("\"DeduplicationKey\" IS NOT NULL");
+
+                    b.HasIndex("DetectedAt");
+
+                    b.HasIndex("TrendSourceId");
+
+                    b.ToTable("TrendItems", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSettings", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("MaxSuggestionsPerCycle")
+                        .HasColumnType("integer");
+
+                    b.Property<bool>("RelevanceFilterEnabled")
+                        .HasColumnType("boolean");
+
+                    b.Property<float>("RelevanceScoreThreshold")
+                        .HasColumnType("real");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.ToTable("TrendSettings", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSource", b =>
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
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("FeedUrl")
+                        .HasColumnType("text");
+
+                    b.Property<bool>("IsEnabled")
+                        .HasColumnType("boolean");
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
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Name", "Type")
+                        .IsUnique();
+
+                    b.ToTable("TrendSources", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestion", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Rationale")
+                        .IsRequired()
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<float>("RelevanceScore")
+                        .HasColumnType("real");
+
+                    b.Property<int>("Status")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
+                    b.Property<int>("SuggestedContentType")
+                        .HasColumnType("integer");
+
+                    b.PrimitiveCollection<int[]>("SuggestedPlatforms")
+                        .IsRequired()
+                        .HasColumnType("integer[]");
+
+                    b.Property<string>("Topic")
+                        .IsRequired()
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Status");
+
+                    b.ToTable("TrendSuggestions", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestionItem", b =>
+                {
+                    b.Property<Guid>("TrendSuggestionId")
+                        .HasColumnType("uuid");
+
+                    b.Property<Guid>("TrendItemId")
+                        .HasColumnType("uuid");
+
+                    b.Property<float>("SimilarityScore")
+                        .HasColumnType("real");
+
+                    b.HasKey("TrendSuggestionId", "TrendItemId");
+
+                    b.HasIndex("TrendItemId");
+
+                    b.ToTable("TrendSuggestionItems", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.User", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<DateTimeOffset>("CreatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("DisplayName")
+                        .IsRequired()
+                        .HasMaxLength(200)
+                        .HasColumnType("character varying(200)");
+
+                    b.Property<string>("Email")
+                        .IsRequired()
+                        .HasMaxLength(256)
+                        .HasColumnType("character varying(256)");
+
+                    b.Property<string>("Settings")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
+                    b.Property<string>("TimeZoneId")
+                        .IsRequired()
+                        .HasMaxLength(100)
+                        .HasColumnType("character varying(100)");
+
+                    b.Property<DateTimeOffset>("UpdatedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<uint>("xmin")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Email")
+                        .IsUnique();
+
+                    b.ToTable("Users", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.WorkflowTransitionLog", b =>
+                {
+                    b.Property<Guid>("Id")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("ActorId")
+                        .HasMaxLength(500)
+                        .HasColumnType("character varying(500)");
+
+                    b.Property<int>("ActorType")
+                        .HasColumnType("integer");
+
+                    b.Property<Guid>("ContentId")
+                        .HasColumnType("uuid");
+
+                    b.Property<int>("FromStatus")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("Reason")
+                        .HasMaxLength(2000)
+                        .HasColumnType("character varying(2000)");
+
+                    b.Property<DateTimeOffset>("Timestamp")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<int>("ToStatus")
+                        .HasColumnType("integer");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("Timestamp");
+
+                    b.HasIndex("ContentId", "Timestamp")
+                        .IsDescending(false, true);
+
+                    b.ToTable("WorkflowTransitionLogs", (string)null);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecution", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.AgentExecutionLog", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.AgentExecution", null)
+                        .WithMany()
+                        .HasForeignKey("AgentExecutionId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.CalendarSlot", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.ContentSeries", null)
+                        .WithMany()
+                        .HasForeignKey("ContentSeriesId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Content", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ParentContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.ContentPlatformStatus", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementAction", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.EngagementExecution", "EngagementExecution")
+                        .WithMany("Actions")
+                        .HasForeignKey("EngagementExecutionId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.Navigation("EngagementExecution");
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementExecution", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.EngagementTask", "EngagementTask")
+                        .WithMany("Executions")
+                        .HasForeignKey("EngagementTaskId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.Navigation("EngagementTask");
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementSnapshot", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.ContentPlatformStatus", null)
+                        .WithMany()
+                        .HasForeignKey("ContentPlatformStatusId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.Notification", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.SetNull);
+
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.User", null)
+                        .WithMany()
+                        .HasForeignKey("UserId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.SavedTrendItem", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendItem", "TrendItem")
+                        .WithMany()
+                        .HasForeignKey("TrendItemId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.Navigation("TrendItem");
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendItem", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendSource", null)
+                        .WithMany()
+                        .HasForeignKey("TrendSourceId")
+                        .OnDelete(DeleteBehavior.SetNull);
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestionItem", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendItem", "TrendItem")
+                        .WithMany()
+                        .HasForeignKey("TrendItemId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.TrendSuggestion", null)
+                        .WithMany("RelatedTrends")
+                        .HasForeignKey("TrendSuggestionId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+
+                    b.Navigation("TrendItem");
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.WorkflowTransitionLog", b =>
+                {
+                    b.HasOne("PersonalBrandAssistant.Domain.Entities.Content", null)
+                        .WithMany()
+                        .HasForeignKey("ContentId")
+                        .OnDelete(DeleteBehavior.Cascade)
+                        .IsRequired();
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementExecution", b =>
+                {
+                    b.Navigation("Actions");
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.EngagementTask", b =>
+                {
+                    b.Navigation("Executions");
+                });
+
+            modelBuilder.Entity("PersonalBrandAssistant.Domain.Entities.TrendSuggestion", b =>
+                {
+                    b.Navigation("RelatedTrends");
+                });
+#pragma warning restore 612, 618
+        }
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
index e3daa89..f5d9a6b 100644
--- a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
+++ b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
@@ -37,6 +37,8 @@
     <PackageReference Include="Cronos" Version="0.11.1" />
     <PackageReference Include="SkiaSharp" Version="3.119.2" />
     <PackageReference Include="System.ServiceModel.Syndication" Version="10.0.5" />
+    <PackageReference Include="Microsoft.Extensions.Caching.Hybrid" Version="9.6.0" />
+    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="9.6.0" />
   </ItemGroup>
 
 </Project>
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/CachedDashboardAggregator.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/CachedDashboardAggregator.cs
new file mode 100644
index 0000000..5d9ee85
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/CachedDashboardAggregator.cs
@@ -0,0 +1,139 @@
+using Microsoft.Extensions.Caching.Hybrid;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+internal sealed class CachedDashboardAggregator(
+    IDashboardAggregator inner,
+    HybridCache cache,
+    ILogger<CachedDashboardAggregator> logger,
+    TimeProvider timeProvider) : IDashboardAggregator, IDashboardCacheInvalidator
+{
+    private static readonly TimeSpan RefreshCooldown = TimeSpan.FromMinutes(1);
+
+    private static readonly HybridCacheEntryOptions SummaryOptions = new()
+    {
+        Expiration = TimeSpan.FromMinutes(30),
+        LocalCacheExpiration = TimeSpan.FromMinutes(5)
+    };
+
+    private static readonly HybridCacheEntryOptions TimelineOptions = new()
+    {
+        Expiration = TimeSpan.FromMinutes(15),
+        LocalCacheExpiration = TimeSpan.FromMinutes(5)
+    };
+
+    private static readonly HybridCacheEntryOptions PlatformSummaryOptions = new()
+    {
+        Expiration = TimeSpan.FromMinutes(15),
+        LocalCacheExpiration = TimeSpan.FromMinutes(5)
+    };
+
+    private DateTimeOffset? _lastRefreshAt;
+
+    public async Task<Result<DashboardSummary>> GetSummaryAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
+    {
+        var key = $"dashboard:summary:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";
+        try
+        {
+            var value = await cache.GetOrCreateAsync<DashboardSummary>(
+                key,
+                async token =>
+                {
+                    var result = await inner.GetSummaryAsync(from, to, token);
+                    return result.IsSuccess
+                        ? result.Value!
+                        : throw new FactoryFailureException(result.ErrorCode, result.Errors);
+                },
+                SummaryOptions,
+                tags: ["dashboard", "social"],
+                cancellationToken: ct);
+            return Result.Success(value);
+        }
+        catch (FactoryFailureException ex)
+        {
+            return Result.Failure<DashboardSummary>(ex.Code, [.. ex.Messages]);
+        }
+    }
+
+    public async Task<Result<IReadOnlyList<DailyEngagement>>> GetTimelineAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
+    {
+        var key = $"dashboard:timeline:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";
+        try
+        {
+            // Cache as List<> (serializable) and return as IReadOnlyList<>
+            var value = await cache.GetOrCreateAsync<List<DailyEngagement>>(
+                key,
+                async token =>
+                {
+                    var result = await inner.GetTimelineAsync(from, to, token);
+                    return result.IsSuccess
+                        ? result.Value!.ToList()
+                        : throw new FactoryFailureException(result.ErrorCode, result.Errors);
+                },
+                TimelineOptions,
+                tags: ["dashboard", "social"],
+                cancellationToken: ct);
+            return Result.Success<IReadOnlyList<DailyEngagement>>(value);
+        }
+        catch (FactoryFailureException ex)
+        {
+            return Result.Failure<IReadOnlyList<DailyEngagement>>(ex.Code, [.. ex.Messages]);
+        }
+    }
+
+    public async Task<Result<IReadOnlyList<PlatformSummary>>> GetPlatformSummariesAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
+    {
+        var key = $"dashboard:platforms:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";
+        try
+        {
+            var value = await cache.GetOrCreateAsync<List<PlatformSummary>>(
+                key,
+                async token =>
+                {
+                    var result = await inner.GetPlatformSummariesAsync(from, to, token);
+                    return result.IsSuccess
+                        ? result.Value!.ToList()
+                        : throw new FactoryFailureException(result.ErrorCode, result.Errors);
+                },
+                PlatformSummaryOptions,
+                tags: ["dashboard", "social"],
+                cancellationToken: ct);
+            return Result.Success<IReadOnlyList<PlatformSummary>>(value);
+        }
+        catch (FactoryFailureException ex)
+        {
+            return Result.Failure<IReadOnlyList<PlatformSummary>>(ex.Code, [.. ex.Messages]);
+        }
+    }
+
+    public async Task<bool> TryInvalidateAsync(CancellationToken ct)
+    {
+        var now = timeProvider.GetUtcNow();
+        if (_lastRefreshAt is not null && now - _lastRefreshAt < RefreshCooldown)
+        {
+            logger.LogWarning(
+                "Dashboard cache refresh rate limited. Last refresh at {LastRefreshAt}",
+                _lastRefreshAt);
+            return false;
+        }
+
+        await cache.RemoveByTagAsync("dashboard", ct);
+        _lastRefreshAt = now;
+        logger.LogInformation("Dashboard cache invalidated");
+        return true;
+    }
+
+    /// <summary>Thrown inside cache factory to prevent caching failures. Never escapes the class.</summary>
+    private sealed class FactoryFailureException(ErrorCode code, IReadOnlyList<string> messages) : Exception
+    {
+        public ErrorCode Code => code;
+        public IReadOnlyList<string> Messages => messages;
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/GoogleAnalyticsService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/GoogleAnalyticsService.cs
index a3dadb8..6b671be 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/GoogleAnalyticsService.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/GoogleAnalyticsService.cs
@@ -7,6 +7,10 @@ using Microsoft.Extensions.Options;
 using PersonalBrandAssistant.Application.Common.Errors;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
+using Polly;
+using Polly.CircuitBreaker;
+using Polly.Retry;
+using Polly.Timeout;
 
 namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
 
@@ -16,6 +20,7 @@ internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
     private readonly ISearchConsoleClient _searchConsoleClient;
     private readonly GoogleAnalyticsOptions _options;
     private readonly ILogger<GoogleAnalyticsService> _logger;
+    private readonly ResiliencePipeline _resiliencePipeline;
 
     public GoogleAnalyticsService(
         IGa4Client ga4Client,
@@ -27,6 +32,37 @@ internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
         _searchConsoleClient = searchConsoleClient;
         _options = options.Value;
         _logger = logger;
+
+        _resiliencePipeline = new ResiliencePipelineBuilder()
+            .AddTimeout(TimeSpan.FromSeconds(15))
+            .AddRetry(new RetryStrategyOptions
+            {
+                MaxRetryAttempts = 2,
+                BackoffType = DelayBackoffType.Exponential,
+                UseJitter = true,
+                ShouldHandle = new PredicateBuilder()
+                    .Handle<RpcException>(ex =>
+                        ex.StatusCode is StatusCode.Unavailable
+                        or StatusCode.DeadlineExceeded
+                        or StatusCode.ResourceExhausted)
+                    .Handle<Google.GoogleApiException>()
+                    .Handle<HttpRequestException>()
+            })
+            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
+            {
+                FailureRatio = 1.0,
+                SamplingDuration = TimeSpan.FromSeconds(30),
+                MinimumThroughput = 3,
+                BreakDuration = TimeSpan.FromSeconds(30),
+                ShouldHandle = new PredicateBuilder()
+                    .Handle<RpcException>(ex =>
+                        ex.StatusCode is StatusCode.Unavailable
+                        or StatusCode.DeadlineExceeded
+                        or StatusCode.ResourceExhausted)
+                    .Handle<Google.GoogleApiException>()
+                    .Handle<HttpRequestException>()
+            })
+            .Build();
     }
 
     public async Task<Result<WebsiteOverview>> GetOverviewAsync(
@@ -56,7 +92,8 @@ internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
                 }
             };
 
-            var response = await _ga4Client.RunReportAsync(request, ct);
+            var response = await _resiliencePipeline.ExecuteAsync(
+                async token => await _ga4Client.RunReportAsync(request, token), ct);
 
             if (response.Rows is null || response.Rows.Count == 0)
             {
@@ -75,6 +112,18 @@ internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
 
             return Result<WebsiteOverview>.Success(overview);
         }
+        catch (BrokenCircuitException ex)
+        {
+            _logger.LogWarning(ex, "GA4 circuit breaker is open — overview request rejected");
+            return Result<WebsiteOverview>.Failure(
+                ErrorCode.InternalError, "GA4 service temporarily unavailable");
+        }
+        catch (TimeoutRejectedException ex)
+        {
+            _logger.LogWarning(ex, "GA4 overview request timed out after retries");
+            return Result<WebsiteOverview>.Failure(
+                ErrorCode.InternalError, "GA4 request timed out");
+        }
         catch (RpcException ex)
         {
             _logger.LogError(ex, "GA4 API error fetching overview");
@@ -122,7 +171,8 @@ internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
                 Limit = limit
             };
 
-            var response = await _ga4Client.RunReportAsync(request, ct);
+            var response = await _resiliencePipeline.ExecuteAsync(
+                async token => await _ga4Client.RunReportAsync(request, token), ct);
 
             var pages = (response.Rows ?? Enumerable.Empty<Row>())
                 .Select(row => new PageViewEntry(
@@ -133,6 +183,18 @@ internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
 
             return Result<IReadOnlyList<PageViewEntry>>.Success(pages);
         }
+        catch (BrokenCircuitException ex)
+        {
+            _logger.LogWarning(ex, "GA4 circuit breaker is open — top pages request rejected");
+            return Result<IReadOnlyList<PageViewEntry>>.Failure(
+                ErrorCode.InternalError, "GA4 service temporarily unavailable");
+        }
+        catch (TimeoutRejectedException ex)
+        {
+            _logger.LogWarning(ex, "GA4 top pages request timed out after retries");
+            return Result<IReadOnlyList<PageViewEntry>>.Failure(
+                ErrorCode.InternalError, "GA4 request timed out");
+        }
         catch (RpcException ex)
         {
             _logger.LogError(ex, "GA4 API error fetching top pages");
@@ -171,7 +233,8 @@ internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
                 }
             };
 
-            var response = await _ga4Client.RunReportAsync(request, ct);
+            var response = await _resiliencePipeline.ExecuteAsync(
+                async token => await _ga4Client.RunReportAsync(request, token), ct);
 
             var sources = (response.Rows ?? Enumerable.Empty<Row>())
                 .Select(row => new TrafficSourceEntry(
@@ -182,6 +245,18 @@ internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
 
             return Result<IReadOnlyList<TrafficSourceEntry>>.Success(sources);
         }
+        catch (BrokenCircuitException ex)
+        {
+            _logger.LogWarning(ex, "GA4 circuit breaker is open — traffic sources request rejected");
+            return Result<IReadOnlyList<TrafficSourceEntry>>.Failure(
+                ErrorCode.InternalError, "GA4 service temporarily unavailable");
+        }
+        catch (TimeoutRejectedException ex)
+        {
+            _logger.LogWarning(ex, "GA4 traffic sources request timed out after retries");
+            return Result<IReadOnlyList<TrafficSourceEntry>>.Failure(
+                ErrorCode.InternalError, "GA4 request timed out");
+        }
         catch (RpcException ex)
         {
             _logger.LogError(ex, "GA4 API error fetching traffic sources");
@@ -209,8 +284,9 @@ internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
                 RowLimit = limit
             };
 
-            var response = await _searchConsoleClient.QueryAsync(
-                _options.SiteUrl, request, ct);
+            var response = await _resiliencePipeline.ExecuteAsync(
+                async token => await _searchConsoleClient.QueryAsync(
+                    _options.SiteUrl, request, token), ct);
 
             var queries = (response.Rows ?? Enumerable.Empty<ApiDataRow>())
                 .Select(row => new SearchQueryEntry(
@@ -223,6 +299,18 @@ internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
 
             return Result<IReadOnlyList<SearchQueryEntry>>.Success(queries);
         }
+        catch (BrokenCircuitException ex)
+        {
+            _logger.LogWarning(ex, "Search Console circuit breaker is open — query request rejected");
+            return Result<IReadOnlyList<SearchQueryEntry>>.Failure(
+                ErrorCode.InternalError, "Search Console service temporarily unavailable");
+        }
+        catch (TimeoutRejectedException ex)
+        {
+            _logger.LogWarning(ex, "Search Console query request timed out after retries");
+            return Result<IReadOnlyList<SearchQueryEntry>>.Failure(
+                ErrorCode.InternalError, "Search Console request timed out");
+        }
         catch (Google.GoogleApiException ex)
         {
             _logger.LogError(ex, "Search Console API error fetching top queries");
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/CachedDashboardAggregatorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/CachedDashboardAggregatorTests.cs
new file mode 100644
index 0000000..4468a2f
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/CachedDashboardAggregatorTests.cs
@@ -0,0 +1,187 @@
+using Microsoft.Extensions.Caching.Hybrid;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.AnalyticsServices;
+
+public class CachedDashboardAggregatorTests : IDisposable
+{
+    private readonly Mock<IDashboardAggregator> _innerMock = new();
+    private readonly Mock<TimeProvider> _timeProviderMock = new();
+    private readonly ServiceProvider _sp;
+    private readonly CachedDashboardAggregator _sut;
+
+    public CachedDashboardAggregatorTests()
+    {
+        _timeProviderMock
+            .Setup(t => t.GetUtcNow())
+            .Returns(new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero));
+
+        var services = new ServiceCollection();
+        services.AddMemoryCache();
+        services.AddHybridCache();
+        services.AddLogging();
+        _sp = services.BuildServiceProvider();
+
+        _sut = new CachedDashboardAggregator(
+            _innerMock.Object,
+            _sp.GetRequiredService<HybridCache>(),
+            NullLogger<CachedDashboardAggregator>.Instance,
+            _timeProviderMock.Object);
+    }
+
+    public void Dispose() => _sp.Dispose();
+
+    [Fact]
+    public async Task GetSummaryAsync_SecondCallReturnsCachedResult()
+    {
+        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
+        var to = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero);
+        var summary = CreateTestSummary();
+
+        _innerMock
+            .Setup(x => x.GetSummaryAsync(from, to, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(summary));
+
+        await _sut.GetSummaryAsync(from, to, CancellationToken.None);
+        var result = await _sut.GetSummaryAsync(from, to, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(summary.TotalEngagement, result.Value!.TotalEngagement);
+        _innerMock.Verify(
+            x => x.GetSummaryAsync(from, to, It.IsAny<CancellationToken>()),
+            Times.Once());
+    }
+
+    [Fact]
+    public async Task GetSummaryAsync_RefreshBypassesCache()
+    {
+        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
+        var to = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero);
+        var summary1 = CreateTestSummary(totalEngagement: 100);
+        var summary2 = CreateTestSummary(totalEngagement: 200);
+
+        _innerMock
+            .SetupSequence(x => x.GetSummaryAsync(from, to, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(summary1))
+            .ReturnsAsync(Result.Success(summary2));
+
+        // First call: cache miss, populates cache
+        var result1 = await _sut.GetSummaryAsync(from, to, CancellationToken.None);
+
+        // Invalidate cache
+        var invalidated = await _sut.TryInvalidateAsync(CancellationToken.None);
+
+        // Second call: cache was invalidated, calls inner again
+        var result2 = await _sut.GetSummaryAsync(from, to, CancellationToken.None);
+
+        Assert.True(invalidated);
+        Assert.Equal(100, result1.Value!.TotalEngagement);
+        Assert.Equal(200, result2.Value!.TotalEngagement);
+        _innerMock.Verify(
+            x => x.GetSummaryAsync(from, to, It.IsAny<CancellationToken>()),
+            Times.Exactly(2));
+    }
+
+    [Fact]
+    public async Task TryInvalidateAsync_RejectsSecondRefreshWithinCooldown()
+    {
+        var now = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);
+        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(now);
+
+        var first = await _sut.TryInvalidateAsync(CancellationToken.None);
+
+        // Move time forward only 30 seconds (within 1-minute cooldown)
+        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(now.AddSeconds(30));
+
+        var second = await _sut.TryInvalidateAsync(CancellationToken.None);
+
+        Assert.True(first);
+        Assert.False(second);
+    }
+
+    [Fact]
+    public async Task TryInvalidateAsync_AllowsRefreshAfterCooldown()
+    {
+        var now = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);
+        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(now);
+
+        var first = await _sut.TryInvalidateAsync(CancellationToken.None);
+
+        // Move time forward 61 seconds (past cooldown)
+        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(now.AddSeconds(61));
+
+        var second = await _sut.TryInvalidateAsync(CancellationToken.None);
+
+        Assert.True(first);
+        Assert.True(second);
+    }
+
+    [Fact]
+    public async Task GetTimelineAsync_CachesResult()
+    {
+        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
+        var to = new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero);
+        var timeline = new List<DailyEngagement>
+        {
+            new(DateOnly.FromDateTime(from.UtcDateTime),
+                [new PlatformDailyMetrics(PlatformType.TwitterX, 5, 2, 1, 8)],
+                8)
+        };
+
+        _innerMock
+            .Setup(x => x.GetTimelineAsync(from, to, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<DailyEngagement>>(timeline));
+
+        await _sut.GetTimelineAsync(from, to, CancellationToken.None);
+        await _sut.GetTimelineAsync(from, to, CancellationToken.None);
+
+        _innerMock.Verify(
+            x => x.GetTimelineAsync(from, to, It.IsAny<CancellationToken>()),
+            Times.Once());
+    }
+
+    [Fact]
+    public async Task GetPlatformSummariesAsync_CachesResult()
+    {
+        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
+        var to = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero);
+        var summaries = new List<PlatformSummary>
+        {
+            new(PlatformType.TwitterX, 1000, 5, 50.0, "Top Post", "https://x.com/1", true)
+        };
+
+        _innerMock
+            .Setup(x => x.GetPlatformSummariesAsync(from, to, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<PlatformSummary>>(summaries));
+
+        await _sut.GetPlatformSummariesAsync(from, to, CancellationToken.None);
+        await _sut.GetPlatformSummariesAsync(from, to, CancellationToken.None);
+
+        _innerMock.Verify(
+            x => x.GetPlatformSummariesAsync(from, to, It.IsAny<CancellationToken>()),
+            Times.Once());
+    }
+
+    private static DashboardSummary CreateTestSummary(int totalEngagement = 500) =>
+        new(
+            TotalEngagement: totalEngagement,
+            PreviousEngagement: 400,
+            TotalImpressions: 5000,
+            PreviousImpressions: 4000,
+            EngagementRate: 10m,
+            PreviousEngagementRate: 10m,
+            ContentPublished: 5,
+            PreviousContentPublished: 4,
+            CostPerEngagement: 0.05m,
+            PreviousCostPerEngagement: 0.06m,
+            WebsiteUsers: 200,
+            PreviousWebsiteUsers: 180,
+            GeneratedAt: DateTimeOffset.UtcNow);
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/ResiliencePolicyTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/ResiliencePolicyTests.cs
new file mode 100644
index 0000000..5f1de59
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/ResiliencePolicyTests.cs
@@ -0,0 +1,76 @@
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.AnalyticsServices;
+
+public class ResiliencePolicyTests
+{
+    [Fact]
+    public async Task SubstackService_RejectsNonSubstackUrl()
+    {
+        var httpClient = new HttpClient();
+        var options = Options.Create(new SubstackOptions { FeedUrl = "https://evil.com/feed" });
+        var logger = new Mock<ILogger<SubstackService>>().Object;
+
+        var service = new SubstackService(httpClient, options, logger);
+        var result = await service.GetRecentPostsAsync(10, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task SubstackService_RejectsHttpUrl()
+    {
+        var httpClient = new HttpClient();
+        var options = Options.Create(new SubstackOptions { FeedUrl = "http://matthewkruczek.substack.com/feed" });
+        var logger = new Mock<ILogger<SubstackService>>().Object;
+
+        var service = new SubstackService(httpClient, options, logger);
+        var result = await service.GetRecentPostsAsync(10, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task SubstackService_RejectsSubdomainSpoofing()
+    {
+        var httpClient = new HttpClient();
+        var options = Options.Create(new SubstackOptions { FeedUrl = "https://substack.com.evil.com/feed" });
+        var logger = new Mock<ILogger<SubstackService>>().Object;
+
+        var service = new SubstackService(httpClient, options, logger);
+        var result = await service.GetRecentPostsAsync(10, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+
+    [Fact]
+    public void SubstackOptions_HasCorrectDefaults()
+    {
+        var options = new SubstackOptions();
+
+        Assert.Equal("Substack", SubstackOptions.SectionName);
+        Assert.Contains("substack.com", options.FeedUrl);
+    }
+
+    [Fact]
+    public void GoogleAnalyticsService_ConstructsWithResiliencePipeline()
+    {
+        // Verify the service can be constructed (which builds the resilience pipeline)
+        var ga4Mock = new Mock<IGa4Client>();
+        var gscMock = new Mock<ISearchConsoleClient>();
+        var options = Options.Create(new GoogleAnalyticsOptions());
+        var logger = new Mock<ILogger<GoogleAnalyticsService>>().Object;
+
+        var service = new GoogleAnalyticsService(ga4Mock.Object, gscMock.Object, options, logger);
+
+        Assert.NotNull(service);
+    }
+}
