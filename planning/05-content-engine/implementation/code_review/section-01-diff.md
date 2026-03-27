diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs
index 51e03b0..2a94060 100644
--- a/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs
@@ -8,7 +8,13 @@ public interface IApplicationDbContext
     DbSet<Content> Contents { get; }
     DbSet<Platform> Platforms { get; }
     DbSet<BrandProfile> BrandProfiles { get; }
-    DbSet<ContentCalendarSlot> ContentCalendarSlots { get; }
+    DbSet<CalendarSlot> CalendarSlots { get; }
+    DbSet<ContentSeries> ContentSeries { get; }
+    DbSet<TrendSource> TrendSources { get; }
+    DbSet<TrendItem> TrendItems { get; }
+    DbSet<TrendSuggestion> TrendSuggestions { get; }
+    DbSet<TrendSuggestionItem> TrendSuggestionItems { get; }
+    DbSet<EngagementSnapshot> EngagementSnapshots { get; }
     DbSet<AuditLogEntry> AuditLogEntries { get; }
     DbSet<User> Users { get; }
     DbSet<Notification> Notifications { get; }
diff --git a/src/PersonalBrandAssistant.Domain/Entities/CalendarSlot.cs b/src/PersonalBrandAssistant.Domain/Entities/CalendarSlot.cs
new file mode 100644
index 0000000..62c62e3
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/CalendarSlot.cs
@@ -0,0 +1,15 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class CalendarSlot : AuditableEntityBase
+{
+    public DateTimeOffset ScheduledAt { get; set; }
+    public PlatformType Platform { get; set; }
+    public Guid? ContentSeriesId { get; set; }
+    public Guid? ContentId { get; set; }
+    public CalendarSlotStatus Status { get; set; } = CalendarSlotStatus.Open;
+    public bool IsOverride { get; set; }
+    public DateTimeOffset? OverriddenOccurrence { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/Content.cs b/src/PersonalBrandAssistant.Domain/Entities/Content.cs
index ab75b6c..e520a5d 100644
--- a/src/PersonalBrandAssistant.Domain/Entities/Content.cs
+++ b/src/PersonalBrandAssistant.Domain/Entities/Content.cs
@@ -39,6 +39,8 @@ public class Content : AuditableEntityBase
     public int RetryCount { get; set; }
     public DateTimeOffset? NextRetryAt { get; set; }
     public DateTimeOffset? PublishingStartedAt { get; set; }
+    public int TreeDepth { get; set; }
+    public PlatformType? RepurposeSourcePlatform { get; set; }
     public uint Version { get; set; }
 
     public static Content Create(
diff --git a/src/PersonalBrandAssistant.Domain/Entities/ContentCalendarSlot.cs b/src/PersonalBrandAssistant.Domain/Entities/ContentCalendarSlot.cs
deleted file mode 100644
index 750e33c..0000000
--- a/src/PersonalBrandAssistant.Domain/Entities/ContentCalendarSlot.cs
+++ /dev/null
@@ -1,17 +0,0 @@
-using PersonalBrandAssistant.Domain.Common;
-using PersonalBrandAssistant.Domain.Enums;
-
-namespace PersonalBrandAssistant.Domain.Entities;
-
-public class ContentCalendarSlot : AuditableEntityBase
-{
-    public DateOnly ScheduledDate { get; set; }
-    public TimeOnly? ScheduledTime { get; set; }
-    public string TimeZoneId { get; set; } = string.Empty;
-    public string? Theme { get; set; }
-    public ContentType ContentType { get; set; }
-    public PlatformType TargetPlatform { get; set; }
-    public Guid? ContentId { get; set; }
-    public bool IsRecurring { get; set; }
-    public string? RecurrencePattern { get; set; }
-}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/ContentSeries.cs b/src/PersonalBrandAssistant.Domain/Entities/ContentSeries.cs
new file mode 100644
index 0000000..aa30a5b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/ContentSeries.cs
@@ -0,0 +1,18 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class ContentSeries : AuditableEntityBase
+{
+    public string Name { get; set; } = string.Empty;
+    public string? Description { get; set; }
+    public string RecurrenceRule { get; set; } = string.Empty;
+    public PlatformType[] TargetPlatforms { get; set; } = [];
+    public ContentType ContentType { get; set; }
+    public List<string> ThemeTags { get; set; } = [];
+    public string TimeZoneId { get; set; } = string.Empty;
+    public bool IsActive { get; set; }
+    public DateTimeOffset StartsAt { get; set; }
+    public DateTimeOffset? EndsAt { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/EngagementSnapshot.cs b/src/PersonalBrandAssistant.Domain/Entities/EngagementSnapshot.cs
new file mode 100644
index 0000000..07b2699
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/EngagementSnapshot.cs
@@ -0,0 +1,14 @@
+using PersonalBrandAssistant.Domain.Common;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class EngagementSnapshot : AuditableEntityBase
+{
+    public Guid ContentPlatformStatusId { get; set; }
+    public int Likes { get; set; }
+    public int Comments { get; set; }
+    public int Shares { get; set; }
+    public int? Impressions { get; set; }
+    public int? Clicks { get; set; }
+    public DateTimeOffset FetchedAt { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/TrendItem.cs b/src/PersonalBrandAssistant.Domain/Entities/TrendItem.cs
new file mode 100644
index 0000000..2b2a8f5
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/TrendItem.cs
@@ -0,0 +1,15 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class TrendItem : AuditableEntityBase
+{
+    public string Title { get; set; } = string.Empty;
+    public string? Description { get; set; }
+    public string? Url { get; set; }
+    public string SourceName { get; set; } = string.Empty;
+    public TrendSourceType SourceType { get; set; }
+    public DateTimeOffset DetectedAt { get; set; }
+    public string? DeduplicationKey { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/TrendSource.cs b/src/PersonalBrandAssistant.Domain/Entities/TrendSource.cs
new file mode 100644
index 0000000..b0e4569
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/TrendSource.cs
@@ -0,0 +1,13 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class TrendSource : AuditableEntityBase
+{
+    public string Name { get; set; } = string.Empty;
+    public TrendSourceType Type { get; set; }
+    public string? ApiUrl { get; set; }
+    public int PollIntervalMinutes { get; set; }
+    public bool IsEnabled { get; set; } = true;
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/TrendSuggestion.cs b/src/PersonalBrandAssistant.Domain/Entities/TrendSuggestion.cs
new file mode 100644
index 0000000..b86630f
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/TrendSuggestion.cs
@@ -0,0 +1,15 @@
+using PersonalBrandAssistant.Domain.Common;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class TrendSuggestion : AuditableEntityBase
+{
+    public string Topic { get; set; } = string.Empty;
+    public string Rationale { get; set; } = string.Empty;
+    public float RelevanceScore { get; set; }
+    public ContentType SuggestedContentType { get; set; }
+    public PlatformType[] SuggestedPlatforms { get; set; } = [];
+    public TrendSuggestionStatus Status { get; set; } = TrendSuggestionStatus.Pending;
+    public ICollection<TrendSuggestionItem> RelatedTrends { get; set; } = new List<TrendSuggestionItem>();
+}
diff --git a/src/PersonalBrandAssistant.Domain/Entities/TrendSuggestionItem.cs b/src/PersonalBrandAssistant.Domain/Entities/TrendSuggestionItem.cs
new file mode 100644
index 0000000..e5d25f5
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Entities/TrendSuggestionItem.cs
@@ -0,0 +1,8 @@
+namespace PersonalBrandAssistant.Domain.Entities;
+
+public class TrendSuggestionItem
+{
+    public Guid TrendSuggestionId { get; set; }
+    public Guid TrendItemId { get; set; }
+    public float SimilarityScore { get; set; }
+}
diff --git a/src/PersonalBrandAssistant.Domain/Enums/CalendarSlotStatus.cs b/src/PersonalBrandAssistant.Domain/Enums/CalendarSlotStatus.cs
new file mode 100644
index 0000000..25bd29d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/CalendarSlotStatus.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum CalendarSlotStatus { Open, Filled, Published, Skipped }
diff --git a/src/PersonalBrandAssistant.Domain/Enums/TrendSourceType.cs b/src/PersonalBrandAssistant.Domain/Enums/TrendSourceType.cs
new file mode 100644
index 0000000..3f82ae8
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/TrendSourceType.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum TrendSourceType { TrendRadar, FreshRSS, Reddit, HackerNews }
diff --git a/src/PersonalBrandAssistant.Domain/Enums/TrendSuggestionStatus.cs b/src/PersonalBrandAssistant.Domain/Enums/TrendSuggestionStatus.cs
new file mode 100644
index 0000000..ee6b43b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Domain/Enums/TrendSuggestionStatus.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Domain.Enums;
+
+public enum TrendSuggestionStatus { Pending, Accepted, Dismissed }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs b/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs
index 035ae0a..41c8e96 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs
@@ -12,7 +12,13 @@ public class ApplicationDbContext : DbContext, IApplicationDbContext
     public DbSet<Content> Contents => Set<Content>();
     public DbSet<Platform> Platforms => Set<Platform>();
     public DbSet<BrandProfile> BrandProfiles => Set<BrandProfile>();
-    public DbSet<ContentCalendarSlot> ContentCalendarSlots => Set<ContentCalendarSlot>();
+    public DbSet<CalendarSlot> CalendarSlots => Set<CalendarSlot>();
+    public DbSet<ContentSeries> ContentSeries => Set<ContentSeries>();
+    public DbSet<TrendSource> TrendSources => Set<TrendSource>();
+    public DbSet<TrendItem> TrendItems => Set<TrendItem>();
+    public DbSet<TrendSuggestion> TrendSuggestions => Set<TrendSuggestion>();
+    public DbSet<TrendSuggestionItem> TrendSuggestionItems => Set<TrendSuggestionItem>();
+    public DbSet<EngagementSnapshot> EngagementSnapshots => Set<EngagementSnapshot>();
     public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
     public DbSet<User> Users => Set<User>();
     public DbSet<Notification> Notifications => Set<Notification>();
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/CalendarSlotConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/CalendarSlotConfiguration.cs
new file mode 100644
index 0000000..8a3bb08
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/CalendarSlotConfiguration.cs
@@ -0,0 +1,41 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class CalendarSlotConfiguration : IEntityTypeConfiguration<CalendarSlot>
+{
+    public void Configure(EntityTypeBuilder<CalendarSlot> builder)
+    {
+        builder.ToTable("CalendarSlots");
+
+        builder.HasKey(s => s.Id);
+
+        builder.Property(s => s.ScheduledAt).IsRequired();
+        builder.Property(s => s.Platform).IsRequired();
+        builder.Property(s => s.Status).IsRequired().HasDefaultValue(CalendarSlotStatus.Open);
+
+        builder.HasIndex(s => s.ScheduledAt);
+        builder.HasIndex(s => s.Status);
+        builder.HasIndex(s => new { s.ScheduledAt, s.Platform });
+
+        builder.HasOne<ContentSeries>()
+            .WithMany()
+            .HasForeignKey(s => s.ContentSeriesId)
+            .OnDelete(DeleteBehavior.SetNull);
+
+        builder.HasOne<Content>()
+            .WithMany()
+            .HasForeignKey(s => s.ContentId)
+            .OnDelete(DeleteBehavior.SetNull);
+
+        builder.Property<uint>("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        builder.Ignore(s => s.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentCalendarSlotConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentCalendarSlotConfiguration.cs
deleted file mode 100644
index d82f983..0000000
--- a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentCalendarSlotConfiguration.cs
+++ /dev/null
@@ -1,33 +0,0 @@
-using Microsoft.EntityFrameworkCore;
-using Microsoft.EntityFrameworkCore.Metadata.Builders;
-using PersonalBrandAssistant.Domain.Entities;
-
-namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
-
-public class ContentCalendarSlotConfiguration : IEntityTypeConfiguration<ContentCalendarSlot>
-{
-    public void Configure(EntityTypeBuilder<ContentCalendarSlot> builder)
-    {
-        builder.ToTable("ContentCalendarSlots");
-
-        builder.HasKey(s => s.Id);
-
-        builder.HasIndex(s => new { s.ScheduledDate, s.TargetPlatform });
-
-        builder.Property(s => s.TimeZoneId).IsRequired().HasMaxLength(100);
-        builder.Property(s => s.Theme).HasMaxLength(200);
-        builder.Property(s => s.RecurrencePattern).HasMaxLength(200);
-
-        builder.HasOne<Content>()
-            .WithMany()
-            .HasForeignKey(s => s.ContentId)
-            .OnDelete(DeleteBehavior.SetNull);
-
-        builder.Property<uint>("xmin")
-            .HasColumnType("xid")
-            .ValueGeneratedOnAddOrUpdate()
-            .IsConcurrencyToken();
-
-        builder.Ignore(s => s.DomainEvents);
-    }
-}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs
index 966254b..2a74b76 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs
@@ -42,12 +42,19 @@ public class ContentConfiguration : IEntityTypeConfiguration<Content>
 
         builder.HasQueryFilter(c => c.Status != ContentStatus.Archived);
 
+        builder.Property(c => c.TreeDepth).IsRequired().HasDefaultValue(0);
+        builder.Property(c => c.RepurposeSourcePlatform);
+
         builder.Property(c => c.ParentContentId);
         builder.HasOne<Content>()
             .WithMany()
             .HasForeignKey(c => c.ParentContentId)
             .OnDelete(DeleteBehavior.SetNull);
 
+        builder.HasIndex(c => new { c.ParentContentId, c.RepurposeSourcePlatform, c.ContentType })
+            .IsUnique()
+            .HasFilter("\"ParentContentId\" IS NOT NULL");
+
         builder.Ignore(c => c.DomainEvents);
     }
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentSeriesConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentSeriesConfiguration.cs
new file mode 100644
index 0000000..a71070f
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentSeriesConfiguration.cs
@@ -0,0 +1,35 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class ContentSeriesConfiguration : IEntityTypeConfiguration<ContentSeries>
+{
+    public void Configure(EntityTypeBuilder<ContentSeries> builder)
+    {
+        builder.ToTable("ContentSeries");
+
+        builder.HasKey(s => s.Id);
+
+        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
+        builder.Property(s => s.Description).HasMaxLength(2000);
+        builder.Property(s => s.RecurrenceRule).IsRequired().HasMaxLength(500);
+        builder.Property(s => s.TargetPlatforms).HasColumnType("integer[]");
+        builder.Property(s => s.ContentType).IsRequired();
+        builder.Property(s => s.ThemeTags)
+            .HasConversion(new JsonValueConverter<List<string>>())
+            .HasColumnType("jsonb");
+        builder.Property(s => s.TimeZoneId).IsRequired().HasMaxLength(100);
+        builder.Property(s => s.IsActive).IsRequired();
+
+        builder.HasIndex(s => s.IsActive);
+
+        builder.Property<uint>("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        builder.Ignore(s => s.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs
new file mode 100644
index 0000000..dc2c492
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs
@@ -0,0 +1,36 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class EngagementSnapshotConfiguration : IEntityTypeConfiguration<EngagementSnapshot>
+{
+    public void Configure(EntityTypeBuilder<EngagementSnapshot> builder)
+    {
+        builder.ToTable("EngagementSnapshots");
+
+        builder.HasKey(e => e.Id);
+
+        builder.Property(e => e.ContentPlatformStatusId).IsRequired();
+        builder.Property(e => e.Likes).IsRequired();
+        builder.Property(e => e.Comments).IsRequired();
+        builder.Property(e => e.Shares).IsRequired();
+        builder.Property(e => e.FetchedAt).IsRequired();
+
+        builder.HasIndex(e => new { e.ContentPlatformStatusId, e.FetchedAt })
+            .IsDescending(false, true);
+
+        builder.HasOne<ContentPlatformStatus>()
+            .WithMany()
+            .HasForeignKey(e => e.ContentPlatformStatusId)
+            .OnDelete(DeleteBehavior.Cascade);
+
+        builder.Property<uint>("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        builder.Ignore(e => e.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendItemConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendItemConfiguration.cs
new file mode 100644
index 0000000..edf1301
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendItemConfiguration.cs
@@ -0,0 +1,33 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class TrendItemConfiguration : IEntityTypeConfiguration<TrendItem>
+{
+    public void Configure(EntityTypeBuilder<TrendItem> builder)
+    {
+        builder.ToTable("TrendItems");
+
+        builder.HasKey(t => t.Id);
+
+        builder.Property(t => t.Title).IsRequired().HasMaxLength(500);
+        builder.Property(t => t.Description).HasMaxLength(4000);
+        builder.Property(t => t.Url).HasMaxLength(2000);
+        builder.Property(t => t.SourceName).IsRequired().HasMaxLength(200);
+        builder.Property(t => t.SourceType).IsRequired();
+        builder.Property(t => t.DetectedAt).IsRequired();
+        builder.Property(t => t.DeduplicationKey).HasMaxLength(128);
+
+        builder.HasIndex(t => t.DeduplicationKey).IsUnique();
+        builder.HasIndex(t => t.DetectedAt);
+
+        builder.Property<uint>("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        builder.Ignore(t => t.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSourceConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSourceConfiguration.cs
new file mode 100644
index 0000000..c5a03b8
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSourceConfiguration.cs
@@ -0,0 +1,29 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class TrendSourceConfiguration : IEntityTypeConfiguration<TrendSource>
+{
+    public void Configure(EntityTypeBuilder<TrendSource> builder)
+    {
+        builder.ToTable("TrendSources");
+
+        builder.HasKey(s => s.Id);
+
+        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
+        builder.Property(s => s.ApiUrl).HasMaxLength(2000);
+        builder.Property(s => s.PollIntervalMinutes).IsRequired();
+        builder.Property(s => s.IsEnabled).IsRequired();
+
+        builder.HasIndex(s => new { s.Name, s.Type }).IsUnique();
+
+        builder.Property<uint>("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        builder.Ignore(s => s.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSuggestionConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSuggestionConfiguration.cs
new file mode 100644
index 0000000..98e6719
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSuggestionConfiguration.cs
@@ -0,0 +1,37 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class TrendSuggestionConfiguration : IEntityTypeConfiguration<TrendSuggestion>
+{
+    public void Configure(EntityTypeBuilder<TrendSuggestion> builder)
+    {
+        builder.ToTable("TrendSuggestions");
+
+        builder.HasKey(s => s.Id);
+
+        builder.Property(s => s.Topic).IsRequired().HasMaxLength(500);
+        builder.Property(s => s.Rationale).IsRequired().HasMaxLength(2000);
+        builder.Property(s => s.RelevanceScore).IsRequired();
+        builder.Property(s => s.SuggestedContentType).IsRequired();
+        builder.Property(s => s.SuggestedPlatforms).HasColumnType("integer[]");
+        builder.Property(s => s.Status).IsRequired().HasDefaultValue(TrendSuggestionStatus.Pending);
+
+        builder.HasMany(s => s.RelatedTrends)
+            .WithOne()
+            .HasForeignKey(si => si.TrendSuggestionId)
+            .OnDelete(DeleteBehavior.Cascade);
+
+        builder.HasIndex(s => s.Status);
+
+        builder.Property<uint>("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        builder.Ignore(s => s.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSuggestionItemConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSuggestionItemConfiguration.cs
new file mode 100644
index 0000000..80bcd8b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSuggestionItemConfiguration.cs
@@ -0,0 +1,20 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class TrendSuggestionItemConfiguration : IEntityTypeConfiguration<TrendSuggestionItem>
+{
+    public void Configure(EntityTypeBuilder<TrendSuggestionItem> builder)
+    {
+        builder.ToTable("TrendSuggestionItems");
+
+        builder.HasKey(si => new { si.TrendSuggestionId, si.TrendItemId });
+
+        builder.HasOne<TrendItem>()
+            .WithMany()
+            .HasForeignKey(si => si.TrendItemId)
+            .OnDelete(DeleteBehavior.Cascade);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/CalendarSlotTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/CalendarSlotTests.cs
new file mode 100644
index 0000000..9d3a588
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/CalendarSlotTests.cs
@@ -0,0 +1,35 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class CalendarSlotTests
+{
+    [Fact]
+    public void CalendarSlot_DefaultStatus_IsOpen()
+    {
+        var slot = new CalendarSlot();
+        Assert.Equal(CalendarSlotStatus.Open, slot.Status);
+    }
+
+    [Fact]
+    public void CalendarSlot_WithOverride_StoresOverriddenOccurrence()
+    {
+        var original = DateTimeOffset.UtcNow;
+        var slot = new CalendarSlot
+        {
+            IsOverride = true,
+            OverriddenOccurrence = original,
+        };
+
+        Assert.True(slot.IsOverride);
+        Assert.Equal(original, slot.OverriddenOccurrence);
+    }
+
+    [Fact]
+    public void CalendarSlot_ContentId_IsNullable()
+    {
+        var slot = new CalendarSlot();
+        Assert.Null(slot.ContentId);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentCalendarSlotTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentCalendarSlotTests.cs
deleted file mode 100644
index 75e2754..0000000
--- a/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentCalendarSlotTests.cs
+++ /dev/null
@@ -1,54 +0,0 @@
-using PersonalBrandAssistant.Domain.Entities;
-using PersonalBrandAssistant.Domain.Enums;
-
-namespace PersonalBrandAssistant.Domain.Tests.Entities;
-
-public class ContentCalendarSlotTests
-{
-    [Fact]
-    public void Slot_WithValidTimeZoneId_CreatesSuccessfully()
-    {
-        var slot = new ContentCalendarSlot
-        {
-            ScheduledDate = new DateOnly(2026, 3, 15),
-            TimeZoneId = "America/New_York",
-            ContentType = ContentType.BlogPost,
-            TargetPlatform = PlatformType.LinkedIn,
-        };
-
-        Assert.Equal("America/New_York", slot.TimeZoneId);
-        Assert.NotEqual(Guid.Empty, slot.Id);
-    }
-
-    [Fact]
-    public void Slot_WithRecurrencePattern_StoresCronString()
-    {
-        var slot = new ContentCalendarSlot
-        {
-            ScheduledDate = new DateOnly(2026, 3, 15),
-            TimeZoneId = "UTC",
-            ContentType = ContentType.SocialPost,
-            TargetPlatform = PlatformType.TwitterX,
-            IsRecurring = true,
-            RecurrencePattern = "0 9 * * 1-5",
-        };
-
-        Assert.True(slot.IsRecurring);
-        Assert.Equal("0 9 * * 1-5", slot.RecurrencePattern);
-    }
-
-    [Fact]
-    public void NonRecurringSlot_HasNullRecurrencePattern()
-    {
-        var slot = new ContentCalendarSlot
-        {
-            ScheduledDate = new DateOnly(2026, 3, 15),
-            TimeZoneId = "UTC",
-            ContentType = ContentType.SocialPost,
-            TargetPlatform = PlatformType.TwitterX,
-        };
-
-        Assert.False(slot.IsRecurring);
-        Assert.Null(slot.RecurrencePattern);
-    }
-}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentSeriesTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentSeriesTests.cs
new file mode 100644
index 0000000..21d4bad
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentSeriesTests.cs
@@ -0,0 +1,30 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class ContentSeriesTests
+{
+    [Fact]
+    public void ContentSeries_Inherits_AuditableEntityBase()
+    {
+        var series = new ContentSeries();
+        Assert.NotEqual(Guid.Empty, series.Id);
+    }
+
+    [Fact]
+    public void ContentSeries_DefaultValues_AreCorrect()
+    {
+        var series = new ContentSeries();
+        Assert.Empty(series.TargetPlatforms);
+        Assert.Empty(series.ThemeTags);
+    }
+
+    [Fact]
+    public void ContentSeries_StoresRecurrenceRule()
+    {
+        var rrule = "FREQ=WEEKLY;BYDAY=TU;BYHOUR=9;BYMINUTE=0";
+        var series = new ContentSeries { RecurrenceRule = rrule };
+        Assert.Equal(rrule, series.RecurrenceRule);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs
index eeded1e..2e045bf 100644
--- a/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs
@@ -144,6 +144,20 @@ public class ContentTests
         }
     }
 
+    [Fact]
+    public void Content_TreeDepth_DefaultsToZero()
+    {
+        var content = CreateDraft();
+        Assert.Equal(0, content.TreeDepth);
+    }
+
+    [Fact]
+    public void Content_RepurposeSourcePlatform_IsNullable()
+    {
+        var content = CreateDraft();
+        Assert.Null(content.RepurposeSourcePlatform);
+    }
+
     private static void TransitionToState(Content content, ContentStatus target)
     {
         if (content.Status == target) return;
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/EngagementSnapshotTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/EngagementSnapshotTests.cs
new file mode 100644
index 0000000..64eb73e
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/EngagementSnapshotTests.cs
@@ -0,0 +1,43 @@
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class EngagementSnapshotTests
+{
+    [Fact]
+    public void EngagementSnapshot_Impressions_IsNullable()
+    {
+        var snapshot = new EngagementSnapshot();
+        Assert.Null(snapshot.Impressions);
+    }
+
+    [Fact]
+    public void EngagementSnapshot_Clicks_IsNullable()
+    {
+        var snapshot = new EngagementSnapshot();
+        Assert.Null(snapshot.Clicks);
+    }
+
+    [Fact]
+    public void EngagementSnapshot_StoresAllEngagementFields()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var snapshot = new EngagementSnapshot
+        {
+            ContentPlatformStatusId = Guid.NewGuid(),
+            Likes = 100,
+            Comments = 25,
+            Shares = 10,
+            Impressions = 5000,
+            Clicks = 200,
+            FetchedAt = now,
+        };
+
+        Assert.Equal(100, snapshot.Likes);
+        Assert.Equal(25, snapshot.Comments);
+        Assert.Equal(10, snapshot.Shares);
+        Assert.Equal(5000, snapshot.Impressions);
+        Assert.Equal(200, snapshot.Clicks);
+        Assert.Equal(now, snapshot.FetchedAt);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendItemTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendItemTests.cs
new file mode 100644
index 0000000..55a90f2
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendItemTests.cs
@@ -0,0 +1,37 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class TrendItemTests
+{
+    [Fact]
+    public void TrendItem_DeduplicationKey_IsDeterministic()
+    {
+        var key = "sha256-abc123";
+        var item = new TrendItem { DeduplicationKey = key };
+        Assert.Equal(key, item.DeduplicationKey);
+    }
+
+    [Fact]
+    public void TrendItem_StoresAllFields()
+    {
+        var now = DateTimeOffset.UtcNow;
+        var item = new TrendItem
+        {
+            Title = "Test Trend",
+            Description = "A test trend item",
+            Url = "https://example.com/trend",
+            SourceName = "r/dotnet",
+            SourceType = TrendSourceType.Reddit,
+            DetectedAt = now,
+        };
+
+        Assert.Equal("Test Trend", item.Title);
+        Assert.Equal("A test trend item", item.Description);
+        Assert.Equal("https://example.com/trend", item.Url);
+        Assert.Equal("r/dotnet", item.SourceName);
+        Assert.Equal(TrendSourceType.Reddit, item.SourceType);
+        Assert.Equal(now, item.DetectedAt);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSourceTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSourceTests.cs
new file mode 100644
index 0000000..0cabbd3
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSourceTests.cs
@@ -0,0 +1,29 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class TrendSourceTests
+{
+    [Fact]
+    public void TrendSource_RequiredFields_AreSet()
+    {
+        var source = new TrendSource
+        {
+            Name = "HN Feed",
+            Type = TrendSourceType.HackerNews,
+            PollIntervalMinutes = 30,
+        };
+
+        Assert.Equal("HN Feed", source.Name);
+        Assert.Equal(TrendSourceType.HackerNews, source.Type);
+        Assert.Equal(30, source.PollIntervalMinutes);
+    }
+
+    [Fact]
+    public void TrendSource_IsEnabled_DefaultsToTrue()
+    {
+        var source = new TrendSource();
+        Assert.True(source.IsEnabled);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSuggestionItemTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSuggestionItemTests.cs
new file mode 100644
index 0000000..52b4a8e
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSuggestionItemTests.cs
@@ -0,0 +1,23 @@
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class TrendSuggestionItemTests
+{
+    [Fact]
+    public void TrendSuggestionItem_MapsJoinRelationship()
+    {
+        var suggestionId = Guid.NewGuid();
+        var itemId = Guid.NewGuid();
+        var join = new TrendSuggestionItem
+        {
+            TrendSuggestionId = suggestionId,
+            TrendItemId = itemId,
+            SimilarityScore = 0.92f,
+        };
+
+        Assert.Equal(suggestionId, join.TrendSuggestionId);
+        Assert.Equal(itemId, join.TrendItemId);
+        Assert.Equal(0.92f, join.SimilarityScore);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSuggestionTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSuggestionTests.cs
new file mode 100644
index 0000000..bfd085f
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSuggestionTests.cs
@@ -0,0 +1,29 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Domain.Tests.Entities;
+
+public class TrendSuggestionTests
+{
+    [Fact]
+    public void TrendSuggestion_DefaultStatus_IsPending()
+    {
+        var suggestion = new TrendSuggestion();
+        Assert.Equal(TrendSuggestionStatus.Pending, suggestion.Status);
+    }
+
+    [Fact]
+    public void TrendSuggestion_StoresRelevanceScore()
+    {
+        var suggestion = new TrendSuggestion { RelevanceScore = 0.85f };
+        Assert.Equal(0.85f, suggestion.RelevanceScore);
+    }
+
+    [Fact]
+    public void TrendSuggestion_RelatedTrends_IsInitialized()
+    {
+        var suggestion = new TrendSuggestion();
+        Assert.NotNull(suggestion.RelatedTrends);
+        Assert.Empty(suggestion.RelatedTrends);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs b/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
index f50ab82..f93d035 100644
--- a/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
+++ b/tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs
@@ -115,4 +115,36 @@ public class EnumTests
         Assert.Contains(PlatformPublishStatus.Skipped, values);
         Assert.Contains(PlatformPublishStatus.Processing, values);
     }
+
+    [Fact]
+    public void TrendSourceType_HasExactly4Values()
+    {
+        var values = Enum.GetValues<TrendSourceType>();
+        Assert.Equal(4, values.Length);
+        Assert.Contains(TrendSourceType.TrendRadar, values);
+        Assert.Contains(TrendSourceType.FreshRSS, values);
+        Assert.Contains(TrendSourceType.Reddit, values);
+        Assert.Contains(TrendSourceType.HackerNews, values);
+    }
+
+    [Fact]
+    public void TrendSuggestionStatus_HasExactly3Values()
+    {
+        var values = Enum.GetValues<TrendSuggestionStatus>();
+        Assert.Equal(3, values.Length);
+        Assert.Contains(TrendSuggestionStatus.Pending, values);
+        Assert.Contains(TrendSuggestionStatus.Accepted, values);
+        Assert.Contains(TrendSuggestionStatus.Dismissed, values);
+    }
+
+    [Fact]
+    public void CalendarSlotStatus_HasExactly4Values()
+    {
+        var values = Enum.GetValues<CalendarSlotStatus>();
+        Assert.Equal(4, values.Length);
+        Assert.Contains(CalendarSlotStatus.Open, values);
+        Assert.Contains(CalendarSlotStatus.Filled, values);
+        Assert.Contains(CalendarSlotStatus.Published, values);
+        Assert.Contains(CalendarSlotStatus.Skipped, values);
+    }
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs
index 3238b6c..b6b7b75 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs
@@ -50,18 +50,51 @@ public class ApplicationDbContextConfigurationTests
     }
 
     [Fact]
-    public void ContentCalendarSlot_HasCompositeIndex()
+    public void CalendarSlot_HasScheduledAtIndex()
     {
         using var context = CreateInMemoryContext();
-        var entityType = context.Model.FindEntityType(typeof(ContentCalendarSlot))!;
+        var entityType = context.Model.FindEntityType(typeof(CalendarSlot))!;
         var index = entityType.GetIndexes()
-            .FirstOrDefault(i => i.Properties.Count == 2 &&
-                i.Properties.Any(p => p.Name == nameof(ContentCalendarSlot.ScheduledDate)) &&
-                i.Properties.Any(p => p.Name == nameof(ContentCalendarSlot.TargetPlatform)));
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(CalendarSlot.ScheduledAt)));
+
+        Assert.NotNull(index);
+    }
+
+    [Fact]
+    public void CalendarSlot_HasStatusIndex()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(CalendarSlot))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(CalendarSlot.Status)));
 
         Assert.NotNull(index);
     }
 
+    [Fact]
+    public void CalendarSlot_HasFkToContent_WithSetNullDelete()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(CalendarSlot))!;
+        var fk = entityType.GetForeignKeys()
+            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "ContentId"));
+
+        Assert.NotNull(fk);
+        Assert.Equal(DeleteBehavior.SetNull, fk!.DeleteBehavior);
+    }
+
+    [Fact]
+    public void CalendarSlot_HasFkToContentSeries_WithSetNullDelete()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(CalendarSlot))!;
+        var fk = entityType.GetForeignKeys()
+            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "ContentSeriesId"));
+
+        Assert.NotNull(fk);
+        Assert.Equal(DeleteBehavior.SetNull, fk!.DeleteBehavior);
+    }
+
     [Fact]
     public void AuditLogEntry_Timestamp_HasIndex()
     {
@@ -275,4 +308,183 @@ public class ApplicationDbContextConfigurationTests
         Assert.Equal("text[]", prop!.GetColumnType());
     }
 
+    [Fact]
+    public void ContentSeries_IsRegistered()
+    {
+        using var context = CreateInMemoryContext();
+        Assert.NotNull(context.Model.FindEntityType(typeof(ContentSeries)));
+    }
+
+    [Fact]
+    public void ContentSeries_TargetPlatforms_HasIntegerArrayColumnType()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(ContentSeries))!;
+        var prop = entityType.FindProperty("TargetPlatforms");
+
+        Assert.NotNull(prop);
+        Assert.Equal("integer[]", prop!.GetColumnType());
+    }
+
+    [Fact]
+    public void ContentSeries_ThemeTags_HasJsonbColumnType()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(ContentSeries))!;
+        var prop = entityType.FindProperty("ThemeTags");
+
+        Assert.NotNull(prop);
+        Assert.Equal("jsonb", prop!.GetColumnType());
+    }
+
+    [Fact]
+    public void ContentSeries_HasIsActiveIndex()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(ContentSeries))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "IsActive"));
+
+        Assert.NotNull(index);
+    }
+
+    [Fact]
+    public void TrendSource_IsRegistered()
+    {
+        using var context = CreateInMemoryContext();
+        Assert.NotNull(context.Model.FindEntityType(typeof(TrendSource)));
+    }
+
+    [Fact]
+    public void TrendSource_HasUniqueIndexOnNameAndType()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(TrendSource))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Count == 2 &&
+                i.Properties.Any(p => p.Name == "Name") &&
+                i.Properties.Any(p => p.Name == "Type"));
+
+        Assert.NotNull(index);
+        Assert.True(index!.IsUnique);
+    }
+
+    [Fact]
+    public void TrendItem_HasUniqueIndexOnDeduplicationKey()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(TrendItem))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "DeduplicationKey"));
+
+        Assert.NotNull(index);
+        Assert.True(index!.IsUnique);
+    }
+
+    [Fact]
+    public void TrendItem_HasDetectedAtIndex()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(TrendItem))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "DetectedAt"));
+
+        Assert.NotNull(index);
+    }
+
+    [Fact]
+    public void TrendSuggestion_SuggestedPlatforms_HasIntegerArrayColumnType()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(TrendSuggestion))!;
+        var prop = entityType.FindProperty("SuggestedPlatforms");
+
+        Assert.NotNull(prop);
+        Assert.Equal("integer[]", prop!.GetColumnType());
+    }
+
+    [Fact]
+    public void TrendSuggestion_HasStatusIndex()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(TrendSuggestion))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Status"));
+
+        Assert.NotNull(index);
+    }
+
+    [Fact]
+    public void TrendSuggestionItem_HasCompositeKey()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(TrendSuggestionItem))!;
+        var pk = entityType.FindPrimaryKey()!;
+
+        Assert.Equal(2, pk.Properties.Count);
+        Assert.Contains(pk.Properties, p => p.Name == "TrendSuggestionId");
+        Assert.Contains(pk.Properties, p => p.Name == "TrendItemId");
+    }
+
+    [Fact]
+    public void EngagementSnapshot_HasCompositeIndex()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(EngagementSnapshot))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Count == 2 &&
+                i.Properties.Any(p => p.Name == "ContentPlatformStatusId") &&
+                i.Properties.Any(p => p.Name == "FetchedAt"));
+
+        Assert.NotNull(index);
+    }
+
+    [Fact]
+    public void EngagementSnapshot_HasFkToContentPlatformStatus_WithCascadeDelete()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(EngagementSnapshot))!;
+        var fk = entityType.GetForeignKeys()
+            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "ContentPlatformStatusId"));
+
+        Assert.NotNull(fk);
+        Assert.Equal(DeleteBehavior.Cascade, fk!.DeleteBehavior);
+    }
+
+    [Fact]
+    public void Content_HasTreeDepthColumn()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(Content))!;
+        var prop = entityType.FindProperty("TreeDepth");
+
+        Assert.NotNull(prop);
+    }
+
+    [Fact]
+    public void Content_HasRepurposeSourcePlatformColumn()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(Content))!;
+        var prop = entityType.FindProperty("RepurposeSourcePlatform");
+
+        Assert.NotNull(prop);
+        Assert.True(prop!.IsNullable);
+    }
+
+    [Fact]
+    public void Content_HasRepurposingUniqueConstraint()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(Content))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Count == 3 &&
+                i.Properties.Any(p => p.Name == "ParentContentId") &&
+                i.Properties.Any(p => p.Name == "RepurposeSourcePlatform") &&
+                i.Properties.Any(p => p.Name == "ContentType"));
+
+        Assert.NotNull(index);
+        Assert.True(index!.IsUnique);
+    }
+
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs
index 8dfa894..3ea3d99 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs
@@ -29,7 +29,13 @@ public class MigrationTests
         Assert.Contains("Contents", tables);
         Assert.Contains("Platforms", tables);
         Assert.Contains("BrandProfiles", tables);
-        Assert.Contains("ContentCalendarSlots", tables);
+        Assert.Contains("CalendarSlots", tables);
+        Assert.Contains("ContentSeries", tables);
+        Assert.Contains("TrendSources", tables);
+        Assert.Contains("TrendItems", tables);
+        Assert.Contains("TrendSuggestions", tables);
+        Assert.Contains("TrendSuggestionItems", tables);
+        Assert.Contains("EngagementSnapshots", tables);
         Assert.Contains("AuditLogEntries", tables);
         Assert.Contains("Users", tables);
     }
