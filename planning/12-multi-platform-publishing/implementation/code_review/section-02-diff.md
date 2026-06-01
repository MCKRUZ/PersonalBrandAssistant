diff --git a/src/PBA.Application/Common/Behaviors/ValidationBehavior.cs b/src/PBA.Application/Common/Behaviors/ValidationBehavior.cs
new file mode 100644
index 0000000..14a5433
--- /dev/null
+++ b/src/PBA.Application/Common/Behaviors/ValidationBehavior.cs
@@ -0,0 +1,49 @@
+using FluentValidation;
+using MediatR;
+using PBA.Domain.Common;
+
+namespace PBA.Application.Common.Behaviors;
+
+public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
+    : IPipelineBehavior<TRequest, TResponse>
+    where TRequest : notnull
+{
+    public async Task<TResponse> Handle(
+        TRequest request,
+        RequestHandlerDelegate<TResponse> next,
+        CancellationToken cancellationToken)
+    {
+        if (!validators.Any())
+            return await next();
+
+        var context = new ValidationContext<TRequest>(request);
+        var results = await Task.WhenAll(
+            validators.Select(v => v.ValidateAsync(context, cancellationToken)));
+
+        var failures = results
+            .SelectMany(r => r.Errors)
+            .Where(f => f is not null)
+            .ToList();
+
+        if (failures.Count == 0)
+            return await next();
+
+        var errors = failures.Select(f => f.ErrorMessage).ToList();
+
+        if (typeof(TResponse).IsGenericType &&
+            typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
+        {
+            var method = typeof(TResponse).GetMethod(
+                nameof(Result<object>.ValidationFailure),
+                [typeof(IReadOnlyList<string>)]);
+            return (TResponse)method!.Invoke(null, [errors.AsReadOnly()])!;
+        }
+
+        if (typeof(TResponse) == typeof(Result))
+        {
+            return (TResponse)(object)Result.ValidationFailure(errors.AsReadOnly());
+        }
+
+        throw new ValidationException(failures);
+    }
+}
diff --git a/src/PBA.Application/Common/Interfaces/IAppDbContext.cs b/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
index d90437f..50b6def 100644
--- a/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
+++ b/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
@@ -7,6 +7,7 @@ public interface IAppDbContext
 {
     DbSet<Content> Contents { get; }
     DbSet<ContentPlatformPublish> ContentPlatformPublishes { get; }
+    DbSet<PlatformCredential> PlatformCredentials { get; }
     DbSet<BrandProfile> BrandProfiles { get; }
     DbSet<Idea> Ideas { get; }
     DbSet<SavedIdea> SavedIdeas { get; }
diff --git a/src/PBA.Application/DependencyInjection.cs b/src/PBA.Application/DependencyInjection.cs
index c888c11..92742ef 100644
--- a/src/PBA.Application/DependencyInjection.cs
+++ b/src/PBA.Application/DependencyInjection.cs
@@ -1,5 +1,7 @@
 using FluentValidation;
+using MediatR;
 using Microsoft.Extensions.DependencyInjection;
+using PBA.Application.Common.Behaviors;
 
 namespace PBA.Application;
 
@@ -9,7 +11,11 @@ public static class DependencyInjection
     {
         var assembly = typeof(DependencyInjection).Assembly;
 
-        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
+        services.AddMediatR(cfg =>
+        {
+            cfg.RegisterServicesFromAssembly(assembly);
+            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
+        });
         services.AddValidatorsFromAssembly(assembly);
 
         return services;
diff --git a/src/PBA.Domain/Entities/Content.cs b/src/PBA.Domain/Entities/Content.cs
index 2ee73ae..1535567 100644
--- a/src/PBA.Domain/Entities/Content.cs
+++ b/src/PBA.Domain/Entities/Content.cs
@@ -15,6 +15,7 @@ public class Content
     public Guid? SourceIdeaId { get; set; }
     public Guid? ParentContentId { get; set; }
     public List<string> Tags { get; set; } = [];
+    public List<Platform> TargetPlatforms { get; set; } = [];
     public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
     public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
     public DateTimeOffset? ScheduledAt { get; set; }
diff --git a/src/PBA.Domain/Entities/ContentPlatformPublish.cs b/src/PBA.Domain/Entities/ContentPlatformPublish.cs
index 50eaf44..d7c3ab1 100644
--- a/src/PBA.Domain/Entities/ContentPlatformPublish.cs
+++ b/src/PBA.Domain/Entities/ContentPlatformPublish.cs
@@ -17,6 +17,8 @@ public class ContentPlatformPublish
     public int Shares { get; set; }
     public int Views { get; set; }
     public DateTimeOffset? MetricsRefreshedAt { get; set; }
+    public int RetryCount { get; set; }
+    public DateTimeOffset? NextRetryAt { get; set; }
 
     public Content? Content { get; set; }
 }
diff --git a/src/PBA.Domain/Entities/PlatformCredential.cs b/src/PBA.Domain/Entities/PlatformCredential.cs
new file mode 100644
index 0000000..55f42cc
--- /dev/null
+++ b/src/PBA.Domain/Entities/PlatformCredential.cs
@@ -0,0 +1,19 @@
+namespace PBA.Domain.Entities;
+
+using PBA.Domain.Enums;
+
+public class PlatformCredential
+{
+    public Guid Id { get; init; } = Guid.NewGuid();
+    public Platform Platform { get; set; }
+    public string EncryptedAccessToken { get; set; } = string.Empty;
+    public string? EncryptedRefreshToken { get; set; }
+    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
+    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
+    public string? Scopes { get; set; }
+    public bool IsActive { get; set; }
+    public string? EncryptedCookies { get; set; }
+    public string? EncryptedIntegrationToken { get; set; }
+    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
+    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
+}
diff --git a/src/PBA.Infrastructure/Data/ApplicationDbContext.cs b/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
index 18304fd..30b87e2 100644
--- a/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
+++ b/src/PBA.Infrastructure/Data/ApplicationDbContext.cs
@@ -14,6 +14,7 @@ public class ApplicationDbContext : DbContext, IAppDbContext
     public DbSet<Idea> Ideas => Set<Idea>();
     public DbSet<SavedIdea> SavedIdeas => Set<SavedIdea>();
     public DbSet<FeedItem> FeedItems => Set<FeedItem>();
+    public DbSet<PlatformCredential> PlatformCredentials => Set<PlatformCredential>();
     public DbSet<BrandProfile> BrandProfiles => Set<BrandProfile>();
 
     protected override void OnModelCreating(ModelBuilder modelBuilder)
diff --git a/src/PBA.Infrastructure/Data/Configurations/ContentConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/ContentConfiguration.cs
index 20caea3..7f0ee37 100644
--- a/src/PBA.Infrastructure/Data/Configurations/ContentConfiguration.cs
+++ b/src/PBA.Infrastructure/Data/Configurations/ContentConfiguration.cs
@@ -12,6 +12,7 @@ public class ContentConfiguration : IEntityTypeConfiguration<Content>
         builder.Property(c => c.Title).IsRequired().HasMaxLength(500);
         builder.Property(c => c.Body).HasColumnType("text");
         builder.Property(c => c.Tags).HasColumnType("jsonb");
+        builder.Property(c => c.TargetPlatforms).HasColumnType("jsonb");
         builder.Property(c => c.VoiceScore).HasPrecision(5, 2);
         builder.Property(c => c.ViralityPrediction).HasPrecision(5, 2);
 
diff --git a/src/PBA.Infrastructure/Data/Configurations/ContentPlatformPublishConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/ContentPlatformPublishConfiguration.cs
index ae56642..02320c8 100644
--- a/src/PBA.Infrastructure/Data/Configurations/ContentPlatformPublishConfiguration.cs
+++ b/src/PBA.Infrastructure/Data/Configurations/ContentPlatformPublishConfiguration.cs
@@ -13,6 +13,8 @@ public class ContentPlatformPublishConfiguration : IEntityTypeConfiguration<Cont
         builder.Property(c => c.PlatformPostId).HasMaxLength(500);
         builder.Property(c => c.ErrorMessage).HasMaxLength(2000);
 
+        builder.Property(c => c.RetryCount).HasDefaultValue(0);
+
         builder.HasIndex(c => new { c.Platform, c.Status });
     }
 }
diff --git a/src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs
new file mode 100644
index 0000000..996d015
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs
@@ -0,0 +1,21 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PBA.Domain.Entities;
+
+namespace PBA.Infrastructure.Data.Configurations;
+
+public class PlatformCredentialConfiguration : IEntityTypeConfiguration<PlatformCredential>
+{
+    public void Configure(EntityTypeBuilder<PlatformCredential> builder)
+    {
+        builder.HasKey(c => c.Id);
+
+        builder.Property(c => c.EncryptedAccessToken).IsRequired().HasMaxLength(4000);
+        builder.Property(c => c.EncryptedRefreshToken).HasMaxLength(4000);
+        builder.Property(c => c.EncryptedCookies).HasMaxLength(8000);
+        builder.Property(c => c.EncryptedIntegrationToken).HasMaxLength(4000);
+        builder.Property(c => c.Scopes).HasMaxLength(1000);
+
+        builder.HasIndex(c => new { c.Platform, c.IsActive });
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Migrations/20260527124807_AddMultiPlatformPublishing.Designer.cs b/src/PBA.Infrastructure/Data/Migrations/20260527124807_AddMultiPlatformPublishing.Designer.cs
new file mode 100644
index 0000000..1b2f869
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Migrations/20260527124807_AddMultiPlatformPublishing.Designer.cs
@@ -0,0 +1,528 @@
+﻿// <auto-generated />
+using System;
+using System.Collections.Generic;
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
+    [Migration("20260527124807_AddMultiPlatformPublishing")]
+    partial class AddMultiPlatformPublishing
+    {
+        /// <inheritdoc />
+        protected override void BuildTargetModel(ModelBuilder modelBuilder)
+        {
+#pragma warning disable 612, 618
+            modelBuilder
+                .HasAnnotation("ProductVersion", "10.0.7")
+                .HasAnnotation("Relational:MaxIdentifierLength", 63);
+
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
+                    b.Property<string>("Category")
+                        .HasColumnType("text");
+
+                    b.Property<string>("DeduplicationKey")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<string>("Description")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset>("DetectedAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<Guid?>("IdeaSourceId")
+                        .HasColumnType("uuid");
+
+                    b.Property<string>("SourceName")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<int>("Status")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("Summary")
+                        .HasColumnType("text");
+
+                    b.PrimitiveCollection<List<string>>("Tags")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.Property<string>("ThumbnailUrl")
+                        .HasColumnType("text");
+
+                    b.Property<string>("Title")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<string>("Url")
+                        .HasColumnType("text");
+
+                    b.HasKey("Id");
+
+                    b.HasIndex("IdeaSourceId");
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
+                        .HasColumnType("text");
+
+                    b.Property<string>("Category")
+                        .IsRequired()
+                        .HasColumnType("text");
+
+                    b.Property<int>("ConsecutiveFailures")
+                        .HasColumnType("integer");
+
+                    b.Property<string>("FeedUrl")
+                        .HasColumnType("text");
+
+                    b.Property<bool>("IsEnabled")
+                        .HasColumnType("boolean");
+
+                    b.Property<string>("LastError")
+                        .HasColumnType("text");
+
+                    b.Property<DateTimeOffset?>("LastPolledAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<DateTimeOffset?>("LastSuccessAt")
+                        .HasColumnType("timestamp with time zone");
+
+                    b.Property<string>("Name")
+                        .IsRequired()
+                        .HasColumnType("text");
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
+                    b.PrimitiveCollection<List<string>>("SuggestedPlatforms")
+                        .IsRequired()
+                        .HasColumnType("text[]");
+
+                    b.PrimitiveCollection<List<string>>("Tags")
+                        .IsRequired()
+                        .HasColumnType("text[]");
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
+            modelBuilder.Entity("PBA.Domain.Entities.Idea", b =>
+                {
+                    b.HasOne("PBA.Domain.Entities.IdeaSource", "IdeaSource")
+                        .WithMany("Ideas")
+                        .HasForeignKey("IdeaSourceId");
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
diff --git a/src/PBA.Infrastructure/Data/Migrations/20260527124807_AddMultiPlatformPublishing.cs b/src/PBA.Infrastructure/Data/Migrations/20260527124807_AddMultiPlatformPublishing.cs
new file mode 100644
index 0000000..33776c3
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Migrations/20260527124807_AddMultiPlatformPublishing.cs
@@ -0,0 +1,372 @@
+﻿using System;
+using System.Collections.Generic;
+using Microsoft.EntityFrameworkCore.Migrations;
+
+#nullable disable
+
+namespace PBA.Infrastructure.Data.Migrations
+{
+    /// <inheritdoc />
+    public partial class AddMultiPlatformPublishing : Migration
+    {
+        /// <inheritdoc />
+        protected override void Up(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.DropForeignKey(
+                name: "FK_Ideas_IdeaSources_IdeaSourceId",
+                table: "Ideas");
+
+            migrationBuilder.DropIndex(
+                name: "IX_Ideas_DeduplicationKey",
+                table: "Ideas");
+
+            migrationBuilder.AlterColumn<List<string>>(
+                name: "Tags",
+                table: "SavedIdeas",
+                type: "text[]",
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "jsonb");
+
+            migrationBuilder.AlterColumn<List<string>>(
+                name: "SuggestedPlatforms",
+                table: "SavedIdeas",
+                type: "text[]",
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "jsonb");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Name",
+                table: "IdeaSources",
+                type: "text",
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "character varying(200)",
+                oldMaxLength: 200);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "LastError",
+                table: "IdeaSources",
+                type: "text",
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "character varying(2000)",
+                oldMaxLength: 2000,
+                oldNullable: true);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "FeedUrl",
+                table: "IdeaSources",
+                type: "text",
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "character varying(2000)",
+                oldMaxLength: 2000,
+                oldNullable: true);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Category",
+                table: "IdeaSources",
+                type: "text",
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "character varying(100)",
+                oldMaxLength: 100);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "ApiUrl",
+                table: "IdeaSources",
+                type: "text",
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "character varying(2000)",
+                oldMaxLength: 2000,
+                oldNullable: true);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Url",
+                table: "Ideas",
+                type: "text",
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "character varying(2000)",
+                oldMaxLength: 2000,
+                oldNullable: true);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Title",
+                table: "Ideas",
+                type: "text",
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "character varying(500)",
+                oldMaxLength: 500);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "ThumbnailUrl",
+                table: "Ideas",
+                type: "text",
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "character varying(2000)",
+                oldMaxLength: 2000,
+                oldNullable: true);
+
+            migrationBuilder.AlterColumn<List<string>>(
+                name: "Tags",
+                table: "Ideas",
+                type: "text[]",
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "jsonb");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "SourceName",
+                table: "Ideas",
+                type: "text",
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "character varying(200)",
+                oldMaxLength: 200);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "DeduplicationKey",
+                table: "Ideas",
+                type: "text",
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "character varying(500)",
+                oldMaxLength: 500);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Category",
+                table: "Ideas",
+                type: "text",
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "character varying(100)",
+                oldMaxLength: 100,
+                oldNullable: true);
+
+            migrationBuilder.AddColumn<string>(
+                name: "TargetPlatforms",
+                table: "Contents",
+                type: "jsonb",
+                nullable: false,
+                defaultValue: "[]");
+
+            migrationBuilder.AddColumn<DateTimeOffset>(
+                name: "NextRetryAt",
+                table: "ContentPlatformPublishes",
+                type: "timestamp with time zone",
+                nullable: true);
+
+            migrationBuilder.AddColumn<int>(
+                name: "RetryCount",
+                table: "ContentPlatformPublishes",
+                type: "integer",
+                nullable: false,
+                defaultValue: 0);
+
+            migrationBuilder.CreateTable(
+                name: "PlatformCredentials",
+                columns: table => new
+                {
+                    Id = table.Column<Guid>(type: "uuid", nullable: false),
+                    Platform = table.Column<int>(type: "integer", nullable: false),
+                    EncryptedAccessToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
+                    EncryptedRefreshToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
+                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    RefreshTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
+                    Scopes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
+                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
+                    EncryptedCookies = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
+                    EncryptedIntegrationToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
+                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
+                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
+                },
+                constraints: table =>
+                {
+                    table.PrimaryKey("PK_PlatformCredentials", x => x.Id);
+                });
+
+            migrationBuilder.CreateIndex(
+                name: "IX_PlatformCredentials_Platform_IsActive",
+                table: "PlatformCredentials",
+                columns: new[] { "Platform", "IsActive" });
+
+            migrationBuilder.AddForeignKey(
+                name: "FK_Ideas_IdeaSources_IdeaSourceId",
+                table: "Ideas",
+                column: "IdeaSourceId",
+                principalTable: "IdeaSources",
+                principalColumn: "Id");
+        }
+
+        /// <inheritdoc />
+        protected override void Down(MigrationBuilder migrationBuilder)
+        {
+            migrationBuilder.DropForeignKey(
+                name: "FK_Ideas_IdeaSources_IdeaSourceId",
+                table: "Ideas");
+
+            migrationBuilder.DropTable(
+                name: "PlatformCredentials");
+
+            migrationBuilder.DropColumn(
+                name: "TargetPlatforms",
+                table: "Contents");
+
+            migrationBuilder.DropColumn(
+                name: "NextRetryAt",
+                table: "ContentPlatformPublishes");
+
+            migrationBuilder.DropColumn(
+                name: "RetryCount",
+                table: "ContentPlatformPublishes");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Tags",
+                table: "SavedIdeas",
+                type: "jsonb",
+                nullable: false,
+                oldClrType: typeof(List<string>),
+                oldType: "text[]");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "SuggestedPlatforms",
+                table: "SavedIdeas",
+                type: "jsonb",
+                nullable: false,
+                oldClrType: typeof(List<string>),
+                oldType: "text[]");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Name",
+                table: "IdeaSources",
+                type: "character varying(200)",
+                maxLength: 200,
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "text");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "LastError",
+                table: "IdeaSources",
+                type: "character varying(2000)",
+                maxLength: 2000,
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "text",
+                oldNullable: true);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "FeedUrl",
+                table: "IdeaSources",
+                type: "character varying(2000)",
+                maxLength: 2000,
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "text",
+                oldNullable: true);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Category",
+                table: "IdeaSources",
+                type: "character varying(100)",
+                maxLength: 100,
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "text");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "ApiUrl",
+                table: "IdeaSources",
+                type: "character varying(2000)",
+                maxLength: 2000,
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "text",
+                oldNullable: true);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Url",
+                table: "Ideas",
+                type: "character varying(2000)",
+                maxLength: 2000,
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "text",
+                oldNullable: true);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Title",
+                table: "Ideas",
+                type: "character varying(500)",
+                maxLength: 500,
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "text");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "ThumbnailUrl",
+                table: "Ideas",
+                type: "character varying(2000)",
+                maxLength: 2000,
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "text",
+                oldNullable: true);
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Tags",
+                table: "Ideas",
+                type: "jsonb",
+                nullable: false,
+                oldClrType: typeof(List<string>),
+                oldType: "text[]");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "SourceName",
+                table: "Ideas",
+                type: "character varying(200)",
+                maxLength: 200,
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "text");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "DeduplicationKey",
+                table: "Ideas",
+                type: "character varying(500)",
+                maxLength: 500,
+                nullable: false,
+                oldClrType: typeof(string),
+                oldType: "text");
+
+            migrationBuilder.AlterColumn<string>(
+                name: "Category",
+                table: "Ideas",
+                type: "character varying(100)",
+                maxLength: 100,
+                nullable: true,
+                oldClrType: typeof(string),
+                oldType: "text",
+                oldNullable: true);
+
+            migrationBuilder.CreateIndex(
+                name: "IX_Ideas_DeduplicationKey",
+                table: "Ideas",
+                column: "DeduplicationKey");
+
+            migrationBuilder.AddForeignKey(
+                name: "FK_Ideas_IdeaSources_IdeaSourceId",
+                table: "Ideas",
+                column: "IdeaSourceId",
+                principalTable: "IdeaSources",
+                principalColumn: "Id",
+                onDelete: ReferentialAction.SetNull);
+        }
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs b/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
index 6951dac..ba21e55 100644
--- a/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
+++ b/src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs
@@ -1,5 +1,6 @@
 ﻿// <auto-generated />
 using System;
+using System.Collections.Generic;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.EntityFrameworkCore.Infrastructure;
 using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
@@ -120,6 +121,10 @@ namespace PBA.Infrastructure.Data.Migrations
                         .IsRequired()
                         .HasColumnType("jsonb");
 
+                    b.PrimitiveCollection<string>("TargetPlatforms")
+                        .IsRequired()
+                        .HasColumnType("jsonb");
+
                     b.Property<string>("Title")
                         .IsRequired()
                         .HasMaxLength(500)
@@ -167,6 +172,9 @@ namespace PBA.Infrastructure.Data.Migrations
                     b.Property<DateTimeOffset?>("MetricsRefreshedAt")
                         .HasColumnType("timestamp with time zone");
 
+                    b.Property<DateTimeOffset?>("NextRetryAt")
+                        .HasColumnType("timestamp with time zone");
+
                     b.Property<int>("Platform")
                         .HasColumnType("integer");
 
@@ -181,6 +189,11 @@ namespace PBA.Infrastructure.Data.Migrations
                         .HasMaxLength(2000)
                         .HasColumnType("character varying(2000)");
 
+                    b.Property<int>("RetryCount")
+                        .ValueGeneratedOnAdd()
+                        .HasColumnType("integer")
+                        .HasDefaultValue(0);
+
                     b.Property<int>("Shares")
                         .HasColumnType("integer");
 
@@ -264,13 +277,11 @@ namespace PBA.Infrastructure.Data.Migrations
                         .HasColumnType("text");
 
                     b.Property<string>("Category")
-                        .HasMaxLength(100)
-                        .HasColumnType("character varying(100)");
+                        .HasColumnType("text");
 
                     b.Property<string>("DeduplicationKey")
                         .IsRequired()
-                        .HasMaxLength(500)
-                        .HasColumnType("character varying(500)");
+                        .HasColumnType("text");
 
                     b.Property<string>("Description")
                         .HasColumnType("text");
@@ -283,8 +294,7 @@ namespace PBA.Infrastructure.Data.Migrations
 
                     b.Property<string>("SourceName")
                         .IsRequired()
-                        .HasMaxLength(200)
-                        .HasColumnType("character varying(200)");
+                        .HasColumnType("text");
 
                     b.Property<int>("Status")
                         .HasColumnType("integer");
@@ -292,27 +302,22 @@ namespace PBA.Infrastructure.Data.Migrations
                     b.Property<string>("Summary")
                         .HasColumnType("text");
 
-                    b.PrimitiveCollection<string>("Tags")
+                    b.PrimitiveCollection<List<string>>("Tags")
                         .IsRequired()
-                        .HasColumnType("jsonb");
+                        .HasColumnType("text[]");
 
                     b.Property<string>("ThumbnailUrl")
-                        .HasMaxLength(2000)
-                        .HasColumnType("character varying(2000)");
+                        .HasColumnType("text");
 
                     b.Property<string>("Title")
                         .IsRequired()
-                        .HasMaxLength(500)
-                        .HasColumnType("character varying(500)");
+                        .HasColumnType("text");
 
                     b.Property<string>("Url")
-                        .HasMaxLength(2000)
-                        .HasColumnType("character varying(2000)");
+                        .HasColumnType("text");
 
                     b.HasKey("Id");
 
-                    b.HasIndex("DeduplicationKey");
-
                     b.HasIndex("IdeaSourceId");
 
                     b.ToTable("Ideas");
@@ -325,27 +330,23 @@ namespace PBA.Infrastructure.Data.Migrations
                         .HasColumnType("uuid");
 
                     b.Property<string>("ApiUrl")
-                        .HasMaxLength(2000)
-                        .HasColumnType("character varying(2000)");
+                        .HasColumnType("text");
 
                     b.Property<string>("Category")
                         .IsRequired()
-                        .HasMaxLength(100)
-                        .HasColumnType("character varying(100)");
+                        .HasColumnType("text");
 
                     b.Property<int>("ConsecutiveFailures")
                         .HasColumnType("integer");
 
                     b.Property<string>("FeedUrl")
-                        .HasMaxLength(2000)
-                        .HasColumnType("character varying(2000)");
+                        .HasColumnType("text");
 
                     b.Property<bool>("IsEnabled")
                         .HasColumnType("boolean");
 
                     b.Property<string>("LastError")
-                        .HasMaxLength(2000)
-                        .HasColumnType("character varying(2000)");
+                        .HasColumnType("text");
 
                     b.Property<DateTimeOffset?>("LastPolledAt")
                         .HasColumnType("timestamp with time zone");
@@ -355,8 +356,7 @@ namespace PBA.Infrastructure.Data.Migrations
 
                     b.Property<string>("Name")
                         .IsRequired()
-                        .HasMaxLength(200)
-                        .HasColumnType("character varying(200)");
+                        .HasColumnType("text");
 
                     b.Property<int>("PollIntervalMinutes")
                         .HasColumnType("integer");
@@ -369,6 +369,58 @@ namespace PBA.Infrastructure.Data.Migrations
                     b.ToTable("IdeaSources");
                 });
 
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
+                    b.HasIndex("Platform", "IsActive");
+
+                    b.ToTable("PlatformCredentials");
+                });
+
             modelBuilder.Entity("PBA.Domain.Entities.SavedIdea", b =>
                 {
                     b.Property<Guid>("Id")
@@ -387,13 +439,13 @@ namespace PBA.Infrastructure.Data.Migrations
                     b.Property<string>("SuggestedAngle")
                         .HasColumnType("text");
 
-                    b.PrimitiveCollection<string>("SuggestedPlatforms")
+                    b.PrimitiveCollection<List<string>>("SuggestedPlatforms")
                         .IsRequired()
-                        .HasColumnType("jsonb");
+                        .HasColumnType("text[]");
 
-                    b.PrimitiveCollection<string>("Tags")
+                    b.PrimitiveCollection<List<string>>("Tags")
                         .IsRequired()
-                        .HasColumnType("jsonb");
+                        .HasColumnType("text[]");
 
                     b.HasKey("Id");
 
@@ -435,8 +487,7 @@ namespace PBA.Infrastructure.Data.Migrations
                 {
                     b.HasOne("PBA.Domain.Entities.IdeaSource", "IdeaSource")
                         .WithMany("Ideas")
-                        .HasForeignKey("IdeaSourceId")
-                        .OnDelete(DeleteBehavior.SetNull);
+                        .HasForeignKey("IdeaSourceId");
 
                     b.Navigation("IdeaSource");
                 });
diff --git a/tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs b/tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs
index 5ffb37b..b022ade 100644
--- a/tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs
+++ b/tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs
@@ -24,7 +24,7 @@ public class ContentEndpointsTests : IClassFixture<TestWebApplicationFactory>
 
     private async Task<Guid> CreateTestContent(
         string title = "Test Content",
-        ContentType contentType = ContentType.BlogPost,
+        ContentType contentType = ContentType.Blog,
         Platform platform = Platform.Blog)
     {
         var body = new CreateContentRequest
@@ -51,7 +51,7 @@ public class ContentEndpointsTests : IClassFixture<TestWebApplicationFactory>
         var body = new CreateContentRequest
         {
             Title = "Integration Test Content",
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
 
@@ -69,7 +69,7 @@ public class ContentEndpointsTests : IClassFixture<TestWebApplicationFactory>
         var body = new CreateContentRequest
         {
             Title = "",
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
 
@@ -89,9 +89,9 @@ public class ContentEndpointsTests : IClassFixture<TestWebApplicationFactory>
     [Fact]
     public async Task GetContentList_RespectsQueryFilters()
     {
-        await CreateTestContent("Filter Test", ContentType.BlogPost, Platform.Blog);
+        await CreateTestContent("Filter Test", ContentType.Blog, Platform.Blog);
 
-        var response = await _client.GetAsync("/api/content?status=Idea&platform=Blog&contentType=BlogPost&search=Filter");
+        var response = await _client.GetAsync("/api/content?status=Idea&platform=Blog&contentType=Blog&search=Filter");
 
         Assert.Equal(HttpStatusCode.OK, response.StatusCode);
     }
diff --git a/tests/PBA.Api.Tests/Endpoints/IdeaSourceEndpointsTests.cs b/tests/PBA.Api.Tests/Endpoints/IdeaSourceEndpointsTests.cs
index 075d3e0..f8165d5 100644
--- a/tests/PBA.Api.Tests/Endpoints/IdeaSourceEndpointsTests.cs
+++ b/tests/PBA.Api.Tests/Endpoints/IdeaSourceEndpointsTests.cs
@@ -42,7 +42,7 @@ public class IdeaSourceEndpointsTests : IClassFixture<TestWebApplicationFactory>
     public async Task DeleteSource_Returns204()
     {
         var createResponse = await _client.PostAsJsonAsync("/api/idea-sources",
-            new IdeaSourceRequest { Name = "Delete Me", Type = IdeaSourceType.Manual, Category = "Test" });
+            new IdeaSourceRequest { Name = "Delete Me", Type = IdeaSourceType.API, Category = "Test" });
         var id = await createResponse.Content.ReadFromJsonAsync<Guid>();
 
         var response = await _client.DeleteAsync($"/api/idea-sources/{id}");
@@ -62,7 +62,7 @@ public class IdeaSourceEndpointsTests : IClassFixture<TestWebApplicationFactory>
     public async Task PutSource_Returns200()
     {
         var createResponse = await _client.PostAsJsonAsync("/api/idea-sources",
-            new IdeaSourceRequest { Name = "Update Me", Type = IdeaSourceType.Manual, Category = "Test" });
+            new IdeaSourceRequest { Name = "Update Me", Type = IdeaSourceType.API, Category = "Test" });
         var id = await createResponse.Content.ReadFromJsonAsync<Guid>();
 
         var updateBody = new IdeaSourceRequest { Name = "Updated Name", Category = "Updated" };
diff --git a/tests/PBA.Api.Tests/Hubs/ContentHubTests.cs b/tests/PBA.Api.Tests/Hubs/ContentHubTests.cs
index f80d289..857f87b 100644
--- a/tests/PBA.Api.Tests/Hubs/ContentHubTests.cs
+++ b/tests/PBA.Api.Tests/Hubs/ContentHubTests.cs
@@ -26,7 +26,7 @@ public class ContentHubTests
         Id = Guid.NewGuid(),
         Title = "Test Post",
         Body = "Some content body",
-        ContentType = ContentType.BlogPost,
+        ContentType = ContentType.Blog,
         PrimaryPlatform = Platform.Blog,
     };
 
diff --git a/tests/PBA.Application.Tests/Features/Content/Commands/CreateContentHandlerTests.cs b/tests/PBA.Application.Tests/Features/Content/Commands/CreateContentHandlerTests.cs
index b9aa512..fadda78 100644
--- a/tests/PBA.Application.Tests/Features/Content/Commands/CreateContentHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Content/Commands/CreateContentHandlerTests.cs
@@ -26,7 +26,7 @@ public class CreateContentHandlerTests
         var handler = new CreateContent.Handler(context);
 
         var command = new CreateContent.Command(
-            "Test Title", ContentType.BlogPost, Platform.Blog, null, ["tag1", "tag2"]);
+            "Test Title", ContentType.Blog, Platform.Blog, null, ["tag1", "tag2"]);
 
         var result = await handler.Handle(command, CancellationToken.None);
 
@@ -34,7 +34,7 @@ public class CreateContentHandlerTests
         var content = await context.Contents.FindAsync(result.Value);
         Assert.NotNull(content);
         Assert.Equal("Test Title", content.Title);
-        Assert.Equal(ContentType.BlogPost, content.ContentType);
+        Assert.Equal(ContentType.Blog, content.ContentType);
         Assert.Equal(Platform.Blog, content.PrimaryPlatform);
         Assert.Equal(["tag1", "tag2"], content.Tags);
     }
@@ -62,14 +62,16 @@ public class CreateContentHandlerTests
         {
             Title = "Idea Title",
             Description = "Idea Description",
-            Status = IdeaStatus.New
+            Status = IdeaStatus.New,
+            DeduplicationKey = "test-key",
+            SourceName = "test-source"
         };
         context.Ideas.Add(idea);
         await context.SaveChangesAsync();
 
         var handler = new CreateContent.Handler(context);
         var command = new CreateContent.Command(
-            "", ContentType.BlogPost, Platform.Blog, idea.Id, []);
+            "", ContentType.Blog, Platform.Blog, idea.Id, []);
 
         var result = await handler.Handle(command, CancellationToken.None);
 
@@ -87,14 +89,16 @@ public class CreateContentHandlerTests
         {
             Title = "Idea",
             Description = "Description",
-            Status = IdeaStatus.New
+            Status = IdeaStatus.New,
+            DeduplicationKey = "test-key-2",
+            SourceName = "test-source"
         };
         context.Ideas.Add(idea);
         await context.SaveChangesAsync();
 
         var handler = new CreateContent.Handler(context);
         var command = new CreateContent.Command(
-            "", ContentType.BlogPost, Platform.Blog, idea.Id, []);
+            "", ContentType.Blog, Platform.Blog, idea.Id, []);
 
         await handler.Handle(command, CancellationToken.None);
 
@@ -124,7 +128,7 @@ public class CreateContentHandlerTests
         var handler = new CreateContent.Handler(context);
 
         var command = new CreateContent.Command(
-            "Title", ContentType.BlogPost, Platform.Blog, Guid.NewGuid(), []);
+            "Title", ContentType.Blog, Platform.Blog, Guid.NewGuid(), []);
 
         var result = await handler.Handle(command, CancellationToken.None);
 
diff --git a/tests/PBA.Application.Tests/Features/Content/Commands/DraftContentHandlerTests.cs b/tests/PBA.Application.Tests/Features/Content/Commands/DraftContentHandlerTests.cs
index 4d6bafc..fae6e52 100644
--- a/tests/PBA.Application.Tests/Features/Content/Commands/DraftContentHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Content/Commands/DraftContentHandlerTests.cs
@@ -31,7 +31,7 @@ public class DraftContentHandlerTests
         {
             Title = "My Post",
             Body = "Some context",
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog,
             Status = ContentStatus.Idea
         };
@@ -62,7 +62,7 @@ public class DraftContentHandlerTests
             Title = "Post",
             Body = "Rough draft",
             Status = ContentStatus.Draft,
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
         context.Contents.Add(content);
@@ -118,7 +118,7 @@ public class DraftContentHandlerTests
             Title = "Post",
             Body = "Brief",
             Status = ContentStatus.Draft,
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
         context.Contents.Add(content);
@@ -146,7 +146,7 @@ public class DraftContentHandlerTests
             Title = "Post",
             Body = "Casual text",
             Status = ContentStatus.Draft,
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
         context.Contents.Add(content);
@@ -182,7 +182,7 @@ public class DraftContentHandlerTests
             Title = "Post",
             Body = "Text",
             Status = ContentStatus.Draft,
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
         context.Contents.Add(content);
@@ -212,7 +212,7 @@ public class DraftContentHandlerTests
             Title = "Post",
             Body = "",
             Status = ContentStatus.Idea,
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
         context.Contents.Add(content);
@@ -239,7 +239,7 @@ public class DraftContentHandlerTests
             Title = "Post",
             Body = "Existing",
             Status = ContentStatus.Draft,
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
         context.Contents.Add(content);
@@ -266,7 +266,7 @@ public class DraftContentHandlerTests
             Title = "Post",
             Body = "Old",
             Status = ContentStatus.Draft,
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
         context.Contents.Add(content);
@@ -293,7 +293,7 @@ public class DraftContentHandlerTests
             Title = "Post",
             Body = "Old",
             Status = ContentStatus.Draft,
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
         context.Contents.Add(content);
@@ -321,7 +321,7 @@ public class DraftContentHandlerTests
             Title = "Post",
             Body = "Text",
             Status = ContentStatus.Draft,
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog
         };
         context.Contents.Add(content);
diff --git a/tests/PBA.Application.Tests/Features/Content/Commands/GenerateCrossPostHandlerTests.cs b/tests/PBA.Application.Tests/Features/Content/Commands/GenerateCrossPostHandlerTests.cs
index 84f6ac1..bff74ad 100644
--- a/tests/PBA.Application.Tests/Features/Content/Commands/GenerateCrossPostHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Content/Commands/GenerateCrossPostHandlerTests.cs
@@ -30,7 +30,7 @@ public class GenerateCrossPostHandlerTests
         {
             Title = "Original Post",
             Body = "Original body content",
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog,
             Status = ContentStatus.Draft
         };
@@ -58,7 +58,7 @@ public class GenerateCrossPostHandlerTests
         {
             Title = "Post",
             Body = "Content",
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog,
             Status = ContentStatus.Draft
         };
@@ -86,7 +86,7 @@ public class GenerateCrossPostHandlerTests
         {
             Title = "Post",
             Body = "Content",
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog,
             Status = ContentStatus.Draft
         };
@@ -114,7 +114,7 @@ public class GenerateCrossPostHandlerTests
         {
             Title = "Post",
             Body = "Content",
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog,
             Status = ContentStatus.Draft
         };
@@ -142,7 +142,7 @@ public class GenerateCrossPostHandlerTests
         {
             Title = "Post",
             Body = "Original",
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog,
             Status = ContentStatus.Draft
         };
@@ -169,7 +169,7 @@ public class GenerateCrossPostHandlerTests
         {
             Title = "Post",
             Body = "Content",
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog,
             Status = ContentStatus.Draft
         };
@@ -192,7 +192,7 @@ public class GenerateCrossPostHandlerTests
         {
             Title = "Post",
             Body = "Content",
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog,
             Status = ContentStatus.Draft
         };
diff --git a/tests/PBA.Application.Tests/Features/Content/Queries/ListContentHandlerTests.cs b/tests/PBA.Application.Tests/Features/Content/Queries/ListContentHandlerTests.cs
index b66e984..0feac4b 100644
--- a/tests/PBA.Application.Tests/Features/Content/Queries/ListContentHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Content/Queries/ListContentHandlerTests.cs
@@ -21,7 +21,7 @@ public class ListContentHandlerTests
         string title = "Test Content",
         ContentStatus status = ContentStatus.Draft,
         Platform platform = Platform.Blog,
-        ContentType contentType = ContentType.BlogPost,
+        ContentType contentType = ContentType.Blog,
         Guid? parentContentId = null,
         bool isDeleted = false,
         DateTimeOffset? updatedAt = null)
@@ -92,16 +92,16 @@ public class ListContentHandlerTests
     public async Task Handle_ContentTypeFilter_ReturnsMatchingOnly()
     {
         await using var context = CreateContext();
-        context.Contents.Add(CreateContent(contentType: ContentType.BlogPost));
+        context.Contents.Add(CreateContent(contentType: ContentType.Blog));
         context.Contents.Add(CreateContent(contentType: ContentType.Tweet));
         await context.SaveChangesAsync();
 
         var handler = new ListContent.Handler(context);
-        var result = await handler.Handle(new ListContent.Query { ContentType = ContentType.BlogPost }, CancellationToken.None);
+        var result = await handler.Handle(new ListContent.Query { ContentType = ContentType.Blog }, CancellationToken.None);
 
         Assert.True(result.IsSuccess);
         Assert.Single(result.Value!.Items);
-        Assert.Equal(ContentType.BlogPost, result.Value.Items[0].ContentType);
+        Assert.Equal(ContentType.Blog, result.Value.Items[0].ContentType);
     }
 
     [Fact]
diff --git a/tests/PBA.Application.Tests/Features/Content/Validators/CreateContentRequestValidatorTests.cs b/tests/PBA.Application.Tests/Features/Content/Validators/CreateContentRequestValidatorTests.cs
index bb73d53..a3a58cd 100644
--- a/tests/PBA.Application.Tests/Features/Content/Validators/CreateContentRequestValidatorTests.cs
+++ b/tests/PBA.Application.Tests/Features/Content/Validators/CreateContentRequestValidatorTests.cs
@@ -13,7 +13,7 @@ public class CreateContentCommandValidatorTests
     [Fact]
     public void Validate_EmptyTitle_HasError()
     {
-        var command = new CreateContent.Command("", ContentType.BlogPost, Platform.Blog, null, []);
+        var command = new CreateContent.Command("", ContentType.Blog, Platform.Blog, null, []);
         var result = _validator.TestValidate(command);
         result.ShouldHaveValidationErrorFor(x => x.Title);
     }
@@ -21,7 +21,7 @@ public class CreateContentCommandValidatorTests
     [Fact]
     public void Validate_TitleExceeds200Chars_HasError()
     {
-        var command = new CreateContent.Command(new string('x', 201), ContentType.BlogPost, Platform.Blog, null, []);
+        var command = new CreateContent.Command(new string('x', 201), ContentType.Blog, Platform.Blog, null, []);
         var result = _validator.TestValidate(command);
         result.ShouldHaveValidationErrorFor(x => x.Title);
     }
@@ -37,7 +37,7 @@ public class CreateContentCommandValidatorTests
     [Fact]
     public void Validate_InvalidPlatform_HasError()
     {
-        var command = new CreateContent.Command("Valid Title", ContentType.BlogPost, (Platform)999, null, []);
+        var command = new CreateContent.Command("Valid Title", ContentType.Blog, (Platform)999, null, []);
         var result = _validator.TestValidate(command);
         result.ShouldHaveValidationErrorFor(x => x.PrimaryPlatform);
     }
@@ -45,7 +45,7 @@ public class CreateContentCommandValidatorTests
     [Fact]
     public void Validate_ValidCommand_NoErrors()
     {
-        var command = new CreateContent.Command("Valid Title", ContentType.BlogPost, Platform.Blog, null, []);
+        var command = new CreateContent.Command("Valid Title", ContentType.Blog, Platform.Blog, null, []);
         var result = _validator.TestValidate(command);
         result.ShouldNotHaveAnyValidationErrors();
     }
diff --git a/tests/PBA.Application.Tests/Features/Feed/Commands/ActOnFeedItemHandlerTests.cs b/tests/PBA.Application.Tests/Features/Feed/Commands/ActOnFeedItemHandlerTests.cs
index 53cd274..8b83420 100644
--- a/tests/PBA.Application.Tests/Features/Feed/Commands/ActOnFeedItemHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Feed/Commands/ActOnFeedItemHandlerTests.cs
@@ -103,7 +103,7 @@ public class ActOnFeedItemHandlerTests
         var item = CreateFeedItem(
             type: FeedItemType.IdeaSuggestion,
             actionTargetId: ideaId,
-            data: @"{""contentType"":""BlogPost"",""primaryPlatform"":""Blog"",""keywords"":[""AI""],""confidence"":0.85,""sourceIdeaTitle"":""Test""}");
+            data: @"{""contentType"":""Blog"",""primaryPlatform"":""Blog"",""keywords"":[""AI""],""confidence"":0.85,""sourceIdeaTitle"":""Test""}");
         context.FeedItems.Add(item);
         await context.SaveChangesAsync();
 
@@ -121,7 +121,7 @@ public class ActOnFeedItemHandlerTests
             s => s.Send(
                 It.Is<CreateContentFromIdea.Command>(c =>
                     c.IdeaId == ideaId &&
-                    c.ContentType == ContentType.BlogPost &&
+                    c.ContentType == ContentType.Blog &&
                     c.PrimaryPlatform == Platform.Blog),
                 It.IsAny<CancellationToken>()),
             Times.Once);
diff --git a/tests/PBA.Application.Tests/Features/Feed/Mappings/FeedMappingsTests.cs b/tests/PBA.Application.Tests/Features/Feed/Mappings/FeedMappingsTests.cs
index 59d6eb0..a2560e6 100644
--- a/tests/PBA.Application.Tests/Features/Feed/Mappings/FeedMappingsTests.cs
+++ b/tests/PBA.Application.Tests/Features/Feed/Mappings/FeedMappingsTests.cs
@@ -53,6 +53,7 @@ public class FeedMappingsTests
         var entity = new FeedItem
         {
             Title = "System notification",
+            Summary = "System notification summary",
             Type = FeedItemType.SystemNotification,
             Data = null,
             ActionType = null,
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Commands/CreateContentFromIdeaTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Commands/CreateContentFromIdeaTests.cs
index 61a6cd9..56137ab 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Commands/CreateContentFromIdeaTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Commands/CreateContentFromIdeaTests.cs
@@ -22,13 +22,13 @@ public class CreateContentFromIdeaTests
     public async Task Handle_CreatesContentFromIdea()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "AI Governance Trends", Description = "Some analysis", DeduplicationKey = "k1" };
+        var idea = new Idea { Title = "AI Governance Trends", Description = "Some analysis", DeduplicationKey = "k1", SourceName = "test-source" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
 
         var handler = new CreateContentFromIdea.Handler(db);
         var result = await handler.Handle(
-            new CreateContentFromIdea.Command(idea.Id, ContentType.BlogPost, Platform.LinkedIn),
+            new CreateContentFromIdea.Command(idea.Id, ContentType.Blog, Platform.LinkedIn),
             CancellationToken.None);
 
         Assert.True(result.IsSuccess);
@@ -36,7 +36,7 @@ public class CreateContentFromIdeaTests
         Assert.NotNull(content);
         Assert.Equal("AI Governance Trends", content.Title);
         Assert.Equal("Some analysis", content.Body);
-        Assert.Equal(ContentType.BlogPost, content.ContentType);
+        Assert.Equal(ContentType.Blog, content.ContentType);
         Assert.Equal(Platform.LinkedIn, content.PrimaryPlatform);
         Assert.Equal(idea.Id, content.SourceIdeaId);
     }
@@ -45,7 +45,7 @@ public class CreateContentFromIdeaTests
     public async Task Handle_SetsBodyToEmptyString_WhenDescriptionIsNull()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "No Description", Description = null, DeduplicationKey = "k2" };
+        var idea = new Idea { Title = "No Description", Description = null, DeduplicationKey = "k2", SourceName = "test-source" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
 
@@ -63,7 +63,7 @@ public class CreateContentFromIdeaTests
     public async Task Handle_SetsContentStatusToIdea()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "Test", DeduplicationKey = "k3" };
+        var idea = new Idea { Title = "Test", DeduplicationKey = "k3", SourceName = "test-source" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
 
@@ -80,13 +80,13 @@ public class CreateContentFromIdeaTests
     public async Task Handle_SetsIdeaStatusToUsed()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "Test", DeduplicationKey = "k4", Status = IdeaStatus.Saved };
+        var idea = new Idea { Title = "Test", DeduplicationKey = "k4", Status = IdeaStatus.Saved, SourceName = "test-source" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
 
         var handler = new CreateContentFromIdea.Handler(db);
         await handler.Handle(
-            new CreateContentFromIdea.Command(idea.Id, ContentType.BlogPost, Platform.Blog),
+            new CreateContentFromIdea.Command(idea.Id, ContentType.Blog, Platform.Blog),
             CancellationToken.None);
 
         var updated = await db.Ideas.FindAsync(idea.Id);
@@ -100,7 +100,7 @@ public class CreateContentFromIdeaTests
         var handler = new CreateContentFromIdea.Handler(db);
 
         var result = await handler.Handle(
-            new CreateContentFromIdea.Command(Guid.NewGuid(), ContentType.BlogPost, Platform.Blog),
+            new CreateContentFromIdea.Command(Guid.NewGuid(), ContentType.Blog, Platform.Blog),
             CancellationToken.None);
 
         Assert.False(result.IsSuccess);
@@ -111,13 +111,13 @@ public class CreateContentFromIdeaTests
     public async Task Handle_ReturnsNewContentId()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "Test", DeduplicationKey = "k5" };
+        var idea = new Idea { Title = "Test", DeduplicationKey = "k5", SourceName = "test-source" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
 
         var handler = new CreateContentFromIdea.Handler(db);
         var result = await handler.Handle(
-            new CreateContentFromIdea.Command(idea.Id, ContentType.BlogPost, Platform.Blog),
+            new CreateContentFromIdea.Command(idea.Id, ContentType.Blog, Platform.Blog),
             CancellationToken.None);
 
         Assert.True(result.IsSuccess);
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Commands/CreateIdeaHandlerTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Commands/CreateIdeaHandlerTests.cs
index 8810806..912df2f 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Commands/CreateIdeaHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Commands/CreateIdeaHandlerTests.cs
@@ -86,7 +86,7 @@ public class CreateIdeaHandlerTests
         var handler = new CreateIdea.Handler(db);
 
         var key = CreateIdea.GenerateDeduplicationKey(null, "Duplicate Idea");
-        db.Ideas.Add(new Idea { Title = "Duplicate Idea", DeduplicationKey = key });
+        db.Ideas.Add(new Idea { Title = "Duplicate Idea", DeduplicationKey = key, SourceName = "test-source" });
         await db.SaveChangesAsync();
 
         var result = await handler.Handle(
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Commands/DeleteIdeaSourceHandlerTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Commands/DeleteIdeaSourceHandlerTests.cs
index 5f0813e..d9e0ba6 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Commands/DeleteIdeaSourceHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Commands/DeleteIdeaSourceHandlerTests.cs
@@ -45,7 +45,8 @@ public class DeleteIdeaSourceHandlerTests
         {
             Title = "Child Idea",
             DeduplicationKey = "child1",
-            IdeaSourceId = source.Id
+            IdeaSourceId = source.Id,
+            SourceName = "test-source"
         };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Commands/DismissIdeaHandlerTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Commands/DismissIdeaHandlerTests.cs
index 5d27901..91239b0 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Commands/DismissIdeaHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Commands/DismissIdeaHandlerTests.cs
@@ -22,7 +22,7 @@ public class DismissIdeaHandlerTests
     public async Task Handle_SetsStatusToDismissed()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "Test", DeduplicationKey = "key1", Status = IdeaStatus.New };
+        var idea = new Idea { Title = "Test", DeduplicationKey = "key1", Status = IdeaStatus.New, SourceName = "test-source" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
 
@@ -39,7 +39,7 @@ public class DismissIdeaHandlerTests
     public async Task Handle_RemovesSavedDetails_WhenTheyExist()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "Test", DeduplicationKey = "key2", Status = IdeaStatus.Saved };
+        var idea = new Idea { Title = "Test", DeduplicationKey = "key2", Status = IdeaStatus.Saved, SourceName = "test-source" };
         idea.SavedDetails = new SavedIdea { IdeaId = idea.Id, Notes = "Keep this" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
@@ -54,7 +54,7 @@ public class DismissIdeaHandlerTests
     public async Task Handle_Succeeds_WhenNoSavedDetails()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "Test", DeduplicationKey = "key3", Status = IdeaStatus.New };
+        var idea = new Idea { Title = "Test", DeduplicationKey = "key3", Status = IdeaStatus.New, SourceName = "test-source" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
 
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Commands/SaveIdeaHandlerTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Commands/SaveIdeaHandlerTests.cs
index 43cd7b5..8097b3a 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Commands/SaveIdeaHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Commands/SaveIdeaHandlerTests.cs
@@ -21,7 +21,7 @@ public class SaveIdeaHandlerTests
     public async Task Handle_CreatesNewSavedIdea_WhenNoExistingSavedDetails()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "Test", DeduplicationKey = "key1" };
+        var idea = new Idea { Title = "Test", DeduplicationKey = "key1", SourceName = "test-source" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
 
@@ -41,7 +41,7 @@ public class SaveIdeaHandlerTests
     public async Task Handle_UpdatesExistingSavedIdea_WhenAlreadySaved()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "Test", DeduplicationKey = "key2" };
+        var idea = new Idea { Title = "Test", DeduplicationKey = "key2", SourceName = "test-source" };
         idea.SavedDetails = new SavedIdea { IdeaId = idea.Id, Notes = "Old notes" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
@@ -62,7 +62,7 @@ public class SaveIdeaHandlerTests
     public async Task Handle_SetsStatusToSaved()
     {
         using var db = CreateContext();
-        var idea = new Idea { Title = "Test", DeduplicationKey = "key3", Status = IdeaStatus.New };
+        var idea = new Idea { Title = "Test", DeduplicationKey = "key3", Status = IdeaStatus.New, SourceName = "test-source" };
         db.Ideas.Add(idea);
         await db.SaveChangesAsync();
 
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Queries/GetIdeaConnectionsHandlerTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Queries/GetIdeaConnectionsHandlerTests.cs
index b1e4c0b..b595ed5 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Queries/GetIdeaConnectionsHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Queries/GetIdeaConnectionsHandlerTests.cs
@@ -37,14 +37,16 @@ public class GetIdeaConnectionsHandlerTests
             Title = "Idea 1",
             Status = IdeaStatus.Saved,
             AIConnections = JsonSerializer.Serialize(connections1),
-            DeduplicationKey = "key-1"
+            DeduplicationKey = "key-1",
+            SourceName = "test-source"
         });
         context.Ideas.Add(new Idea
         {
             Title = "Idea 2",
             Status = IdeaStatus.Saved,
             AIConnections = JsonSerializer.Serialize(connections2),
-            DeduplicationKey = "key-2"
+            DeduplicationKey = "key-2",
+            SourceName = "test-source"
         });
         await context.SaveChangesAsync();
 
@@ -66,7 +68,8 @@ public class GetIdeaConnectionsHandlerTests
             Title = "New Idea",
             Status = IdeaStatus.New,
             AIConnections = "[{\"Theme\":\"Test\"}]",
-            DeduplicationKey = "key-new"
+            DeduplicationKey = "key-new",
+            SourceName = "test-source"
         });
         await context.SaveChangesAsync();
 
@@ -91,14 +94,16 @@ public class GetIdeaConnectionsHandlerTests
             Title = "Valid",
             Status = IdeaStatus.Saved,
             AIConnections = JsonSerializer.Serialize(validConnections),
-            DeduplicationKey = "key-valid"
+            DeduplicationKey = "key-valid",
+            SourceName = "test-source"
         });
         context.Ideas.Add(new Idea
         {
             Title = "Broken",
             Status = IdeaStatus.Saved,
             AIConnections = "not valid json {{{",
-            DeduplicationKey = "key-broken"
+            DeduplicationKey = "key-broken",
+            SourceName = "test-source"
         });
         await context.SaveChangesAsync();
 
@@ -120,10 +125,10 @@ public class GetIdeaConnectionsHandlerTests
             new() { Theme = "Saved Theme", RelatedIdeaIds = [], SuggestedAngle = "Angle", Confidence = 0.8 }
         });
 
-        context.Ideas.Add(new Idea { Title = "Saved", Status = IdeaStatus.Saved, AIConnections = connections, DeduplicationKey = "k1" });
-        context.Ideas.Add(new Idea { Title = "New", Status = IdeaStatus.New, AIConnections = connections, DeduplicationKey = "k2" });
-        context.Ideas.Add(new Idea { Title = "Used", Status = IdeaStatus.Used, AIConnections = connections, DeduplicationKey = "k3" });
-        context.Ideas.Add(new Idea { Title = "Dismissed", Status = IdeaStatus.Dismissed, AIConnections = connections, DeduplicationKey = "k4" });
+        context.Ideas.Add(new Idea { Title = "Saved", Status = IdeaStatus.Saved, AIConnections = connections, DeduplicationKey = "k1", SourceName = "test-source" });
+        context.Ideas.Add(new Idea { Title = "New", Status = IdeaStatus.New, AIConnections = connections, DeduplicationKey = "k2", SourceName = "test-source" });
+        context.Ideas.Add(new Idea { Title = "Used", Status = IdeaStatus.Used, AIConnections = connections, DeduplicationKey = "k3", SourceName = "test-source" });
+        context.Ideas.Add(new Idea { Title = "Dismissed", Status = IdeaStatus.Dismissed, AIConnections = connections, DeduplicationKey = "k4", SourceName = "test-source" });
         await context.SaveChangesAsync();
 
         var handler = new GetIdeaConnections.Handler(context);
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Queries/GetIdeaHandlerTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Queries/GetIdeaHandlerTests.cs
index 627489d..eb43373 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Queries/GetIdeaHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Queries/GetIdeaHandlerTests.cs
@@ -88,7 +88,8 @@ public class GetIdeaHandlerTests
         {
             Title = "Connected Idea",
             AIConnections = JsonSerializer.Serialize(connections),
-            DeduplicationKey = "connected-key"
+            DeduplicationKey = "connected-key",
+            SourceName = "test-source"
         };
         context.Ideas.Add(idea);
         await context.SaveChangesAsync();
@@ -111,7 +112,8 @@ public class GetIdeaHandlerTests
         {
             Title = "Unsaved Idea",
             Status = IdeaStatus.New,
-            DeduplicationKey = "unsaved-key"
+            DeduplicationKey = "unsaved-key",
+            SourceName = "test-source"
         };
         context.Ideas.Add(idea);
         await context.SaveChangesAsync();
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeaSourcesHandlerTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeaSourcesHandlerTests.cs
index b9bc5fd..b35e9e6 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeaSourcesHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeaSourcesHandlerTests.cs
@@ -23,7 +23,7 @@ public class ListIdeaSourcesHandlerTests
         await using var context = CreateContext();
         context.IdeaSources.Add(new IdeaSource { Name = "Zeta Feed", Type = IdeaSourceType.RSS, IsEnabled = true });
         context.IdeaSources.Add(new IdeaSource { Name = "Alpha Feed", Type = IdeaSourceType.API, IsEnabled = true });
-        context.IdeaSources.Add(new IdeaSource { Name = "Beta Feed", Type = IdeaSourceType.Manual, IsEnabled = false });
+        context.IdeaSources.Add(new IdeaSource { Name = "Beta Feed", Type = IdeaSourceType.API, IsEnabled = false });
         await context.SaveChangesAsync();
 
         var handler = new ListIdeaSources.Handler(context);
@@ -95,7 +95,8 @@ public class ListIdeaSourcesHandlerTests
                 {
                     Title = $"Idea {i}",
                     IdeaSourceId = source.Id,
-                    DeduplicationKey = $"dedup-{i}"
+                    DeduplicationKey = $"dedup-{i}",
+                    SourceName = "test-source"
                 });
             }
             await seedContext.SaveChangesAsync();
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeasHandlerTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeasHandlerTests.cs
index 5d92daf..e8fe5fc 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeasHandlerTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Queries/ListIdeasHandlerTests.cs
@@ -35,7 +35,8 @@ public class ListIdeasHandlerTests
             DetectedAt = detectedAt ?? DateTimeOffset.UtcNow,
             Description = description,
             Summary = summary,
-            DeduplicationKey = Guid.NewGuid().ToString()
+            DeduplicationKey = Guid.NewGuid().ToString(),
+            SourceName = "test-source"
         };
     }
 
diff --git a/tests/PBA.Application.Tests/Features/Ideas/Validators/CreateIdeaSourceValidatorTests.cs b/tests/PBA.Application.Tests/Features/Ideas/Validators/CreateIdeaSourceValidatorTests.cs
index 037b661..1f081b9 100644
--- a/tests/PBA.Application.Tests/Features/Ideas/Validators/CreateIdeaSourceValidatorTests.cs
+++ b/tests/PBA.Application.Tests/Features/Ideas/Validators/CreateIdeaSourceValidatorTests.cs
@@ -73,7 +73,7 @@ public class CreateIdeaSourceValidatorTests
         var command = new CreateIdeaSource.Command
         {
             Name = "Manual Source",
-            Type = IdeaSourceType.Manual,
+            Type = IdeaSourceType.API,
             FeedUrl = null,
             PollIntervalMinutes = 30
         };
@@ -87,7 +87,7 @@ public class CreateIdeaSourceValidatorTests
         var command = new CreateIdeaSource.Command
         {
             Name = "Source",
-            Type = IdeaSourceType.Manual,
+            Type = IdeaSourceType.API,
             PollIntervalMinutes = 4
         };
         var result = _validator.TestValidate(command);
@@ -100,7 +100,7 @@ public class CreateIdeaSourceValidatorTests
         var command = new CreateIdeaSource.Command
         {
             Name = "Source",
-            Type = IdeaSourceType.Manual,
+            Type = IdeaSourceType.API,
             PollIntervalMinutes = 1441
         };
         var result = _validator.TestValidate(command);
@@ -113,7 +113,7 @@ public class CreateIdeaSourceValidatorTests
         var command = new CreateIdeaSource.Command
         {
             Name = "Source",
-            Type = IdeaSourceType.Manual,
+            Type = IdeaSourceType.API,
             PollIntervalMinutes = 60
         };
         var result = _validator.TestValidate(command);
diff --git a/tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs b/tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs
index 371978e..92d7dbd 100644
--- a/tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Connectors/BlogConnectorTests.cs
@@ -54,7 +54,7 @@ public class BlogConnectorTests : IDisposable
         {
             Title = title,
             Body = body,
-            ContentType = ContentType.BlogPost,
+            ContentType = ContentType.Blog,
             PrimaryPlatform = Platform.Blog,
             Tags = ["AI", "Engineering"],
             CreatedAt = new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero)
@@ -89,7 +89,7 @@ public class BlogConnectorTests : IDisposable
         Assert.Contains("2026-05-11", result);
         Assert.Contains("Matt Kruczek", result);
         Assert.Contains("AI, Engineering", result);
-        Assert.Contains("BlogPost", result);
+        Assert.Contains("Blog", result);
     }
 
     [Fact]
diff --git a/tests/PBA.Infrastructure.Tests/Data/PlatformCredentialPersistenceTests.cs b/tests/PBA.Infrastructure.Tests/Data/PlatformCredentialPersistenceTests.cs
new file mode 100644
index 0000000..86a31c9
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Data/PlatformCredentialPersistenceTests.cs
@@ -0,0 +1,181 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Data;
+
+public class PlatformCredentialPersistenceTests
+{
+    private static ApplicationDbContext CreateContext()
+    {
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
+            .Options;
+
+        return new ApplicationDbContext(options);
+    }
+
+    [Fact]
+    public async Task PlatformCredential_CanBePersisted_AndRetrieved()
+    {
+        using var context = CreateContext();
+        var now = DateTimeOffset.UtcNow;
+        var credential = new PlatformCredential
+        {
+            Platform = Platform.LinkedIn,
+            EncryptedAccessToken = "enc_access_token_123",
+            EncryptedRefreshToken = "enc_refresh_token_456",
+            AccessTokenExpiresAt = now.AddHours(1),
+            RefreshTokenExpiresAt = now.AddDays(30),
+            Scopes = "r_liteprofile w_member_social",
+            IsActive = true,
+            EncryptedCookies = "enc_cookies_789",
+            EncryptedIntegrationToken = "enc_integration_abc"
+        };
+
+        context.PlatformCredentials.Add(credential);
+        await context.SaveChangesAsync();
+
+        var loaded = await context.PlatformCredentials.FindAsync(credential.Id);
+        Assert.NotNull(loaded);
+        Assert.Equal(Platform.LinkedIn, loaded.Platform);
+        Assert.Equal("enc_access_token_123", loaded.EncryptedAccessToken);
+        Assert.Equal("enc_refresh_token_456", loaded.EncryptedRefreshToken);
+        Assert.Equal(now.AddHours(1), loaded.AccessTokenExpiresAt);
+        Assert.Equal(now.AddDays(30), loaded.RefreshTokenExpiresAt);
+        Assert.Equal("r_liteprofile w_member_social", loaded.Scopes);
+        Assert.True(loaded.IsActive);
+        Assert.Equal("enc_cookies_789", loaded.EncryptedCookies);
+        Assert.Equal("enc_integration_abc", loaded.EncryptedIntegrationToken);
+    }
+
+    [Fact]
+    public async Task PlatformCredential_OnlyOneActivePerPlatform_CanBeQueried()
+    {
+        using var context = CreateContext();
+        var active = new PlatformCredential
+        {
+            Platform = Platform.LinkedIn,
+            EncryptedAccessToken = "active_token",
+            IsActive = true
+        };
+        var inactive = new PlatformCredential
+        {
+            Platform = Platform.LinkedIn,
+            EncryptedAccessToken = "old_token",
+            IsActive = false
+        };
+
+        context.PlatformCredentials.AddRange(active, inactive);
+        await context.SaveChangesAsync();
+
+        var results = await context.PlatformCredentials
+            .Where(c => c.Platform == Platform.LinkedIn && c.IsActive)
+            .ToListAsync();
+
+        Assert.Single(results);
+        Assert.Equal("active_token", results[0].EncryptedAccessToken);
+    }
+
+    [Fact]
+    public async Task Content_TargetPlatforms_SerializesAndDeserializes()
+    {
+        using var context = CreateContext();
+        var content = new Content
+        {
+            Title = "Multi-platform post",
+            TargetPlatforms = [Platform.Blog, Platform.Medium, Platform.LinkedIn]
+        };
+
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        var loaded = await context.Contents.FindAsync(content.Id);
+        Assert.NotNull(loaded);
+        Assert.Equal(3, loaded.TargetPlatforms.Count);
+        Assert.Equal(Platform.Blog, loaded.TargetPlatforms[0]);
+        Assert.Equal(Platform.Medium, loaded.TargetPlatforms[1]);
+        Assert.Equal(Platform.LinkedIn, loaded.TargetPlatforms[2]);
+    }
+
+    [Fact]
+    public void Content_TargetPlatforms_DefaultsToEmptyList()
+    {
+        var content = new Content { Title = "Test" };
+        Assert.NotNull(content.TargetPlatforms);
+        Assert.Empty(content.TargetPlatforms);
+    }
+
+    [Fact]
+    public void ContentPlatformPublish_RetryCount_DefaultsToZero()
+    {
+        var publish = new ContentPlatformPublish { ContentId = Guid.NewGuid() };
+        Assert.Equal(0, publish.RetryCount);
+    }
+
+    [Fact]
+    public void ContentPlatformPublish_NextRetryAt_DefaultsToNull()
+    {
+        var publish = new ContentPlatformPublish { ContentId = Guid.NewGuid() };
+        Assert.Null(publish.NextRetryAt);
+    }
+
+    [Fact]
+    public async Task ContentPlatformPublish_RetryFields_PersistCorrectly()
+    {
+        using var context = CreateContext();
+        var content = new Content { Title = "Test" };
+        context.Contents.Add(content);
+        var futureRetry = DateTimeOffset.UtcNow.AddMinutes(30);
+        var publish = new ContentPlatformPublish
+        {
+            ContentId = content.Id,
+            Platform = Platform.Medium,
+            RetryCount = 2,
+            NextRetryAt = futureRetry
+        };
+
+        context.ContentPlatformPublishes.Add(publish);
+        await context.SaveChangesAsync();
+
+        var loaded = await context.ContentPlatformPublishes.FindAsync(publish.Id);
+        Assert.NotNull(loaded);
+        Assert.Equal(2, loaded.RetryCount);
+        Assert.Equal(futureRetry, loaded.NextRetryAt);
+    }
+
+    [Fact]
+    public void IAppDbContext_Exposes_PlatformCredentials_DbSet()
+    {
+        using var context = CreateContext();
+        Application.Common.Interfaces.IAppDbContext appContext = context;
+        Assert.NotNull(appContext.PlatformCredentials);
+    }
+
+    [Fact]
+    public void Platform_Enum_ContainsMedium()
+    {
+        Assert.True(Enum.IsDefined(typeof(Platform), Platform.Medium));
+        Assert.True(Enum.TryParse<Platform>("Medium", out var parsed));
+        Assert.Equal(Platform.Medium, parsed);
+    }
+
+    [Fact]
+    public void PlatformCredential_Has_CompositeIndex_On_Platform_IsActive()
+    {
+        using var context = CreateContext();
+        var entity = context.Model.FindEntityType(typeof(PlatformCredential))!;
+        var indexes = entity.GetIndexes().ToList();
+
+        var compositeIndex = indexes.FirstOrDefault(i =>
+        {
+            var props = i.Properties.Select(p => p.Name).ToList();
+            return props.Contains(nameof(PlatformCredential.Platform))
+                && props.Contains(nameof(PlatformCredential.IsActive));
+        });
+
+        Assert.NotNull(compositeIndex);
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Services/AiConnectionsServiceTests.cs b/tests/PBA.Infrastructure.Tests/Services/AiConnectionsServiceTests.cs
index 13b42f3..ce77e52 100644
--- a/tests/PBA.Infrastructure.Tests/Services/AiConnectionsServiceTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Services/AiConnectionsServiceTests.cs
@@ -57,6 +57,7 @@ public class AiConnectionsServiceTests : IDisposable
                 Status = status,
                 DetectedAt = DateTimeOffset.UtcNow.AddHours(-i),
                 DeduplicationKey = $"key-{i}",
+                SourceName = "test-source",
             });
         }
         _dbContext.SaveChanges();
@@ -104,6 +105,7 @@ public class AiConnectionsServiceTests : IDisposable
             Tags = ["ai", "governance"],
             Status = IdeaStatus.Saved,
             DeduplicationKey = "k1",
+            SourceName = "test-source",
         });
         _dbContext.SaveChanges();
 
@@ -126,8 +128,8 @@ public class AiConnectionsServiceTests : IDisposable
     [Fact]
     public async Task AnalyzeConnectionsAsync_ParsesValidJsonAndUpdatesIdeas()
     {
-        var idea1 = new Idea { Title = "Idea A", Status = IdeaStatus.Saved, DeduplicationKey = "k1" };
-        var idea2 = new Idea { Title = "Idea B", Status = IdeaStatus.Saved, DeduplicationKey = "k2" };
+        var idea1 = new Idea { Title = "Idea A", Status = IdeaStatus.Saved, DeduplicationKey = "k1", SourceName = "test-source" };
+        var idea2 = new Idea { Title = "Idea B", Status = IdeaStatus.Saved, DeduplicationKey = "k2", SourceName = "test-source" };
         _dbContext.Ideas.AddRange(idea1, idea2);
         _dbContext.SaveChanges();
 
@@ -160,7 +162,7 @@ public class AiConnectionsServiceTests : IDisposable
     [Fact]
     public async Task AnalyzeConnectionsAsync_StripsMarkdownFences()
     {
-        var idea = new Idea { Title = "Test", Status = IdeaStatus.Saved, DeduplicationKey = "k1" };
+        var idea = new Idea { Title = "Test", Status = IdeaStatus.Saved, DeduplicationKey = "k1", SourceName = "test-source" };
         _dbContext.Ideas.Add(idea);
         _dbContext.SaveChanges();
 
@@ -215,7 +217,7 @@ public class AiConnectionsServiceTests : IDisposable
     [Fact]
     public async Task AnalyzeConnectionsAsync_SkipsUnknownIdeaIds()
     {
-        var idea = new Idea { Title = "Known", Status = IdeaStatus.Saved, DeduplicationKey = "k1" };
+        var idea = new Idea { Title = "Known", Status = IdeaStatus.Saved, DeduplicationKey = "k1", SourceName = "test-source" };
         _dbContext.Ideas.Add(idea);
         _dbContext.SaveChanges();
 
