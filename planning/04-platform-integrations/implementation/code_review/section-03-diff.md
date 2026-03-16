diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs
index 4d2521a..51e03b0 100644
--- a/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs
@@ -16,6 +16,8 @@ public interface IApplicationDbContext
     DbSet<AutonomyConfiguration> AutonomyConfigurations { get; }
     DbSet<AgentExecution> AgentExecutions { get; }
     DbSet<AgentExecutionLog> AgentExecutionLogs { get; }
+    DbSet<ContentPlatformStatus> ContentPlatformStatuses { get; }
+    DbSet<OAuthState> OAuthStates { get; }
 
     Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs b/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs
index 5b7a1b3..035ae0a 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs
@@ -20,6 +20,8 @@ public class ApplicationDbContext : DbContext, IApplicationDbContext
     public DbSet<AutonomyConfiguration> AutonomyConfigurations => Set<AutonomyConfiguration>();
     public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();
     public DbSet<AgentExecutionLog> AgentExecutionLogs => Set<AgentExecutionLog>();
+    public DbSet<ContentPlatformStatus> ContentPlatformStatuses => Set<ContentPlatformStatus>();
+    public DbSet<OAuthState> OAuthStates => Set<OAuthState>();
 
     protected override void OnModelCreating(ModelBuilder modelBuilder)
     {
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs
new file mode 100644
index 0000000..8602ee6
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs
@@ -0,0 +1,40 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class ContentPlatformStatusConfiguration : IEntityTypeConfiguration<ContentPlatformStatus>
+{
+    public void Configure(EntityTypeBuilder<ContentPlatformStatus> builder)
+    {
+        builder.ToTable("ContentPlatformStatuses");
+
+        builder.HasKey(c => c.Id);
+
+        builder.Property(c => c.ContentId).IsRequired();
+        builder.Property(c => c.Platform).IsRequired();
+        builder.Property(c => c.Status).IsRequired().HasDefaultValue(PlatformPublishStatus.Pending);
+        builder.Property(c => c.PlatformPostId).HasMaxLength(500);
+        builder.Property(c => c.PostUrl).HasMaxLength(2000);
+        builder.Property(c => c.ErrorMessage).HasMaxLength(4000);
+        builder.Property(c => c.IdempotencyKey).HasMaxLength(200);
+        builder.Property(c => c.RetryCount).IsRequired().HasDefaultValue(0);
+
+        builder.HasIndex(c => new { c.ContentId, c.Platform });
+        builder.HasIndex(c => c.IdempotencyKey).IsUnique();
+
+        builder.HasOne<Content>()
+            .WithMany()
+            .HasForeignKey(c => c.ContentId)
+            .OnDelete(DeleteBehavior.Cascade);
+
+        builder.Property<uint>("xmin")
+            .HasColumnType("xid")
+            .ValueGeneratedOnAddOrUpdate()
+            .IsConcurrencyToken();
+
+        builder.Ignore(c => c.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/OAuthStateConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/OAuthStateConfiguration.cs
new file mode 100644
index 0000000..b4eded0
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/OAuthStateConfiguration.cs
@@ -0,0 +1,26 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Metadata.Builders;
+using PersonalBrandAssistant.Domain.Entities;
+
+namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;
+
+public class OAuthStateConfiguration : IEntityTypeConfiguration<OAuthState>
+{
+    public void Configure(EntityTypeBuilder<OAuthState> builder)
+    {
+        builder.ToTable("OAuthStates");
+
+        builder.HasKey(o => o.Id);
+
+        builder.Property(o => o.State).IsRequired().HasMaxLength(200);
+        builder.Property(o => o.Platform).IsRequired();
+        builder.Property(o => o.CodeVerifier).HasMaxLength(200);
+        builder.Property(o => o.CreatedAt).IsRequired();
+        builder.Property(o => o.ExpiresAt).IsRequired();
+
+        builder.HasIndex(o => o.State).IsUnique();
+        builder.HasIndex(o => o.ExpiresAt);
+
+        builder.Ignore(o => o.DomainEvents);
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs
index 1428bb1..13825ca 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs
@@ -25,6 +25,9 @@ public class PlatformConfiguration : IEntityTypeConfiguration<Platform>
             .HasConversion(new JsonValueConverter<Domain.ValueObjects.PlatformSettings>())
             .HasColumnType("jsonb");
 
+        builder.Property(p => p.GrantedScopes)
+            .HasColumnType("text[]");
+
         builder.Property<uint>("xmin")
             .HasColumnType("xid")
             .ValueGeneratedOnAddOrUpdate()
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs
index bcf1dac..d702b47 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs
@@ -178,4 +178,114 @@ public class ApplicationDbContextConfigurationTests
         Assert.Equal(18, costProperty.GetPrecision());
         Assert.Equal(6, costProperty.GetScale());
     }
+
+    [Fact]
+    public void ContentPlatformStatus_IsRegistered()
+    {
+        using var context = CreateInMemoryContext();
+        Assert.NotNull(context.Model.FindEntityType(typeof(ContentPlatformStatus)));
+    }
+
+    [Fact]
+    public void ContentPlatformStatus_HasCompositeIndexOnContentIdAndPlatform()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(ContentPlatformStatus))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Count == 2 &&
+                i.Properties.Any(p => p.Name == "ContentId") &&
+                i.Properties.Any(p => p.Name == "Platform"));
+
+        Assert.NotNull(index);
+    }
+
+    [Fact]
+    public void ContentPlatformStatus_HasUniqueIndexOnIdempotencyKey()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(ContentPlatformStatus))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "IdempotencyKey"));
+
+        Assert.NotNull(index);
+        Assert.True(index!.IsUnique);
+    }
+
+    [Fact]
+    public void ContentPlatformStatus_HasXminConcurrencyToken()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(ContentPlatformStatus))!;
+        var xmin = entityType.FindProperty("xmin");
+
+        Assert.NotNull(xmin);
+        Assert.True(xmin!.IsConcurrencyToken);
+    }
+
+    [Fact]
+    public void ContentPlatformStatus_HasFkToContent_WithCascadeDelete()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(ContentPlatformStatus))!;
+        var fk = entityType.GetForeignKeys()
+            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "ContentId"));
+
+        Assert.NotNull(fk);
+        Assert.Equal(DeleteBehavior.Cascade, fk!.DeleteBehavior);
+    }
+
+    [Fact]
+    public void OAuthState_IsRegistered()
+    {
+        using var context = CreateInMemoryContext();
+        Assert.NotNull(context.Model.FindEntityType(typeof(OAuthState)));
+    }
+
+    [Fact]
+    public void OAuthState_HasUniqueIndexOnState()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(OAuthState))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "State"));
+
+        Assert.NotNull(index);
+        Assert.True(index!.IsUnique);
+    }
+
+    [Fact]
+    public void OAuthState_HasIndexOnExpiresAt()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(OAuthState))!;
+        var index = entityType.GetIndexes()
+            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "ExpiresAt"));
+
+        Assert.NotNull(index);
+    }
+
+    [Fact]
+    public void Platform_GrantedScopes_HasTextArrayColumnType()
+    {
+        using var context = CreateInMemoryContext();
+        var entityType = context.Model.FindEntityType(typeof(Platform))!;
+        var prop = entityType.FindProperty("GrantedScopes");
+
+        Assert.NotNull(prop);
+        Assert.Equal("text[]", prop!.GetColumnType());
+    }
+
+    [Fact]
+    public void DbContext_IncludesContentPlatformStatusesDbSet()
+    {
+        using var context = CreateInMemoryContext();
+        Assert.NotNull(context.Model.FindEntityType(typeof(ContentPlatformStatus)));
+    }
+
+    [Fact]
+    public void DbContext_IncludesOAuthStatesDbSet()
+    {
+        using var context = CreateInMemoryContext();
+        Assert.NotNull(context.Model.FindEntityType(typeof(OAuthState)));
+    }
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs
index 70dff83..524bdff 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs
@@ -140,6 +140,33 @@ public static class TestEntityFactory
         return execution;
     }
 
+    public static ContentPlatformStatus CreateContentPlatformStatus(
+        Guid contentId,
+        PlatformType platform = PlatformType.TwitterX,
+        PlatformPublishStatus status = PlatformPublishStatus.Pending,
+        string? idempotencyKey = null) =>
+        new()
+        {
+            ContentId = contentId,
+            Platform = platform,
+            Status = status,
+            IdempotencyKey = idempotencyKey ?? $"{contentId}:{platform}:1",
+        };
+
+    public static OAuthState CreateOAuthState(
+        PlatformType platform = PlatformType.TwitterX,
+        string? state = null,
+        DateTimeOffset? expiresAt = null,
+        string? codeVerifier = null) =>
+        new()
+        {
+            State = state ?? Guid.NewGuid().ToString("N"),
+            Platform = platform,
+            CreatedAt = DateTimeOffset.UtcNow,
+            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10),
+            CodeVerifier = codeVerifier,
+        };
+
     public static AgentExecutionLog CreateAgentExecutionLog(
         Guid? agentExecutionId = null,
         int stepNumber = 1,
