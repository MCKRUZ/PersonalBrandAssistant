diff --git a/src/PBA.Application/Common/Interfaces/IAppDbContext.cs b/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
index cecaf58..44088f9 100644
--- a/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
+++ b/src/PBA.Application/Common/Interfaces/IAppDbContext.cs
@@ -6,6 +6,8 @@ namespace PBA.Application.Common.Interfaces;
 public interface IAppDbContext
 {
     DbSet<Content> Contents { get; }
+    DbSet<ContentPlatformPublish> ContentPlatformPublishes { get; }
+    DbSet<BrandProfile> BrandProfiles { get; }
     DbSet<Idea> Ideas { get; }
     DbSet<SavedIdea> SavedIdeas { get; }
     DbSet<IdeaSource> IdeaSources { get; }
diff --git a/src/PBA.Domain/Entities/Content.cs b/src/PBA.Domain/Entities/Content.cs
new file mode 100644
index 0000000..fdc9e8a
--- /dev/null
+++ b/src/PBA.Domain/Entities/Content.cs
@@ -0,0 +1,29 @@
+namespace PBA.Domain.Entities;
+
+using PBA.Domain.Enums;
+
+public class Content
+{
+    public Guid Id { get; init; } = Guid.NewGuid();
+    public required string Title { get; set; }
+    public string Body { get; set; } = string.Empty;
+    public ContentType ContentType { get; set; }
+    public ContentStatus Status { get; set; } = ContentStatus.Idea;
+    public Platform PrimaryPlatform { get; set; }
+    public decimal? VoiceScore { get; set; }
+    public decimal? ViralityPrediction { get; set; }
+    public Guid? SourceIdeaId { get; set; }
+    public Guid? ParentContentId { get; set; }
+    public List<string> Tags { get; set; } = [];
+    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
+    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
+    public DateTimeOffset? ScheduledAt { get; set; }
+    public DateTimeOffset? PublishedAt { get; set; }
+    public string? HangfireJobId { get; set; }
+    public bool IsDeleted { get; set; }
+
+    public Idea? SourceIdea { get; set; }
+    public Content? ParentContent { get; set; }
+    public List<Content> Children { get; set; } = [];
+    public IReadOnlyList<ContentPlatformPublish> CrossPosts { get; set; } = [];
+}
diff --git a/src/PBA.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs
new file mode 100644
index 0000000..cac6ee4
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs
@@ -0,0 +1,28 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PBA.Domain.Entities;
+
+namespace PBA.Infrastructure.Data.Configurations;
+
+public class BrandProfileConfiguration : IEntityTypeConfiguration<BrandProfile>
+{
+    public void Configure(EntityTypeBuilder<BrandProfile> builder)
+    {
+        builder.HasKey(b => b.Id);
+        builder.Property(b => b.Personality).HasColumnType("text");
+        builder.Property(b => b.Tone).HasMaxLength(500);
+        builder.Property(b => b.Topics).HasColumnType("jsonb");
+        builder.Property(b => b.Vocabulary).HasColumnType("jsonb");
+        builder.Property(b => b.AvoidWords).HasColumnType("jsonb");
+        builder.Property(b => b.ExamplePosts).HasColumnType("text");
+        builder.Property(b => b.LearningLog).HasColumnType("text");
+
+        builder.HasData(new BrandProfile
+        {
+            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
+            Personality = string.Empty,
+            Tone = string.Empty,
+            UpdatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
+        });
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Configurations/ContentConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/ContentConfiguration.cs
new file mode 100644
index 0000000..20caea3
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Configurations/ContentConfiguration.cs
@@ -0,0 +1,37 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PBA.Domain.Entities;
+
+namespace PBA.Infrastructure.Data.Configurations;
+
+public class ContentConfiguration : IEntityTypeConfiguration<Content>
+{
+    public void Configure(EntityTypeBuilder<Content> builder)
+    {
+        builder.HasKey(c => c.Id);
+        builder.Property(c => c.Title).IsRequired().HasMaxLength(500);
+        builder.Property(c => c.Body).HasColumnType("text");
+        builder.Property(c => c.Tags).HasColumnType("jsonb");
+        builder.Property(c => c.VoiceScore).HasPrecision(5, 2);
+        builder.Property(c => c.ViralityPrediction).HasPrecision(5, 2);
+
+        builder.HasOne(c => c.SourceIdea)
+            .WithMany()
+            .HasForeignKey(c => c.SourceIdeaId)
+            .OnDelete(DeleteBehavior.SetNull);
+
+        builder.Property(c => c.HangfireJobId).HasMaxLength(200);
+
+        builder.HasOne(c => c.ParentContent)
+            .WithMany(c => c.Children)
+            .HasForeignKey(c => c.ParentContentId)
+            .OnDelete(DeleteBehavior.SetNull);
+
+        builder.HasMany(c => c.CrossPosts)
+            .WithOne(p => p.Content)
+            .HasForeignKey(p => p.ContentId)
+            .OnDelete(DeleteBehavior.Cascade);
+
+        builder.HasQueryFilter(c => !c.IsDeleted);
+    }
+}
diff --git a/src/PBA.Infrastructure/Data/Configurations/ContentPlatformPublishConfiguration.cs b/src/PBA.Infrastructure/Data/Configurations/ContentPlatformPublishConfiguration.cs
new file mode 100644
index 0000000..ae56642
--- /dev/null
+++ b/src/PBA.Infrastructure/Data/Configurations/ContentPlatformPublishConfiguration.cs
@@ -0,0 +1,18 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PBA.Domain.Entities;
+
+namespace PBA.Infrastructure.Data.Configurations;
+
+public class ContentPlatformPublishConfiguration : IEntityTypeConfiguration<ContentPlatformPublish>
+{
+    public void Configure(EntityTypeBuilder<ContentPlatformPublish> builder)
+    {
+        builder.HasKey(c => c.Id);
+        builder.Property(c => c.PublishedUrl).HasMaxLength(2000);
+        builder.Property(c => c.PlatformPostId).HasMaxLength(500);
+        builder.Property(c => c.ErrorMessage).HasMaxLength(2000);
+
+        builder.HasIndex(c => new { c.Platform, c.Status });
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Data/SchemaUpdateTests.cs b/tests/PBA.Infrastructure.Tests/Data/SchemaUpdateTests.cs
new file mode 100644
index 0000000..1b88ba0
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Data/SchemaUpdateTests.cs
@@ -0,0 +1,140 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Infrastructure;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Data;
+
+public class SchemaUpdateTests
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
+    public void IAppDbContext_Exposes_ContentPlatformPublishes_DbSet()
+    {
+        using var context = CreateContext();
+        Application.Common.Interfaces.IAppDbContext appContext = context;
+        Assert.NotNull(appContext.ContentPlatformPublishes);
+    }
+
+    [Fact]
+    public void IAppDbContext_Exposes_BrandProfiles_DbSet()
+    {
+        using var context = CreateContext();
+        Application.Common.Interfaces.IAppDbContext appContext = context;
+        Assert.NotNull(appContext.BrandProfiles);
+    }
+
+    [Fact]
+    public async Task Content_Has_HangfireJobId_Property()
+    {
+        using var context = CreateContext();
+        var content = new Content { Title = "Test", HangfireJobId = "job-123" };
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        var loaded = await context.Contents.FindAsync(content.Id);
+        Assert.Equal("job-123", loaded!.HangfireJobId);
+    }
+
+    [Fact]
+    public async Task Content_Has_IsDeleted_Property_DefaultFalse()
+    {
+        using var context = CreateContext();
+        var content = new Content { Title = "Test" };
+        Assert.False(content.IsDeleted);
+
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        var loaded = await context.Contents.FindAsync(content.Id);
+        Assert.False(loaded!.IsDeleted);
+    }
+
+    [Fact]
+    public async Task Content_Has_Children_NavigationProperty()
+    {
+        using var context = CreateContext();
+        var parent = new Content { Title = "Parent" };
+        var child1 = new Content { Title = "Child 1", ParentContentId = parent.Id };
+        var child2 = new Content { Title = "Child 2", ParentContentId = parent.Id };
+
+        context.Contents.AddRange(parent, child1, child2);
+        await context.SaveChangesAsync();
+
+        var loaded = await context.Contents
+            .Include(c => c.Children)
+            .FirstAsync(c => c.Id == parent.Id);
+
+        Assert.Equal(2, loaded.Children.Count);
+    }
+
+    [Fact]
+    public async Task SoftDelete_QueryFilter_Excludes_IsDeleted_Content()
+    {
+        using var context = CreateContext();
+        var active = new Content { Title = "Active" };
+        var deleted = new Content { Title = "Deleted", IsDeleted = true };
+
+        context.Contents.AddRange(active, deleted);
+        await context.SaveChangesAsync();
+
+        var results = await context.Contents.ToListAsync();
+        Assert.Single(results);
+        Assert.Equal("Active", results[0].Title);
+    }
+
+    [Fact]
+    public async Task SoftDelete_Filter_Can_Be_Overridden_With_IgnoreQueryFilters()
+    {
+        using var context = CreateContext();
+        var active = new Content { Title = "Active" };
+        var deleted = new Content { Title = "Deleted", IsDeleted = true };
+
+        context.Contents.AddRange(active, deleted);
+        await context.SaveChangesAsync();
+
+        var results = await context.Contents.IgnoreQueryFilters().ToListAsync();
+        Assert.Equal(2, results.Count);
+    }
+
+    [Fact]
+    public void ContentPlatformPublish_Has_Composite_Index_On_Platform_Status()
+    {
+        using var context = CreateContext();
+        var entity = context.Model.FindEntityType(typeof(ContentPlatformPublish))!;
+        var indexes = entity.GetIndexes().ToList();
+
+        var compositeIndex = indexes.FirstOrDefault(i =>
+        {
+            var props = i.Properties.Select(p => p.Name).ToList();
+            return props.Contains(nameof(ContentPlatformPublish.Platform))
+                && props.Contains(nameof(ContentPlatformPublish.Status));
+        });
+
+        Assert.NotNull(compositeIndex);
+    }
+
+    [Fact]
+    public void BrandProfile_Has_Seeded_Default_Row()
+    {
+        using var context = CreateContext();
+        var modelSource = context.GetService<IModelSource>();
+        var dependencies = context.GetService<ModelCreationDependencies>();
+        var designTimeModel = modelSource.GetModel(context, dependencies, designTime: true);
+        var entity = designTimeModel.FindEntityType(typeof(BrandProfile))!;
+        var seedData = entity.GetSeedData().ToList();
+
+        Assert.Single(seedData);
+        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), seedData[0][nameof(BrandProfile.Id)]);
+    }
+}
