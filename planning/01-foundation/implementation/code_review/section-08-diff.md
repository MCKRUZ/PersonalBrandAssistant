diff --git a/planning/01-foundation/implementation/deep_implement_config.json b/planning/01-foundation/implementation/deep_implement_config.json
index ab601d8..34ec661 100644
--- a/planning/01-foundation/implementation/deep_implement_config.json
+++ b/planning/01-foundation/implementation/deep_implement_config.json
@@ -33,6 +33,18 @@
     "section-04-infrastructure": {
       "status": "complete",
       "commit_hash": "bd22618"
+    },
+    "section-05-api": {
+      "status": "complete",
+      "commit_hash": "5b04808"
+    },
+    "section-06-docker": {
+      "status": "complete",
+      "commit_hash": "29dff90"
+    },
+    "section-07-angular": {
+      "status": "complete",
+      "commit_hash": "c294a64"
     }
   },
   "pre_commit": {
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/SwaggerTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/SwaggerTests.cs
new file mode 100644
index 0000000..f3f2c38
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/SwaggerTests.cs
@@ -0,0 +1,28 @@
+using System.Net;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+[Collection("Postgres")]
+public class SwaggerTests
+{
+    private readonly PostgresFixture _fixture;
+
+    public SwaggerTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+    }
+
+    [Fact]
+    public async Task Swagger_InDevelopment_ReturnsOk()
+    {
+        var connStr = _fixture.GetUniqueConnectionString();
+        await using var factory = new CustomWebApplicationFactory(connStr);
+        await factory.EnsureDatabaseCreatedAsync();
+
+        var client = factory.CreateAuthenticatedClient();
+        var response = await client.GetAsync("/swagger/index.html");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ConcurrencyTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ConcurrencyTests.cs
new file mode 100644
index 0000000..6242360
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ConcurrencyTests.cs
@@ -0,0 +1,72 @@
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+using PersonalBrandAssistant.Infrastructure.Tests.Utilities;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Persistence;
+
+[Collection("Postgres")]
+public class ConcurrencyTests
+{
+    private readonly PostgresFixture _fixture;
+
+    public ConcurrencyTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+    }
+
+    [Fact]
+    public async Task ConcurrentUpdate_SameContent_ThrowsDbUpdateConcurrencyException()
+    {
+        var connStr = _fixture.GetUniqueConnectionString();
+
+        await using var setupContext = _fixture.CreateDbContext(connectionString: connStr);
+        await setupContext.Database.EnsureCreatedAsync();
+
+        var content = TestEntityFactory.CreateContent();
+        setupContext.Contents.Add(content);
+        await setupContext.SaveChangesAsync();
+        var contentId = content.Id;
+
+        await using var context1 = _fixture.CreateDbContext(connectionString: connStr);
+        await using var context2 = _fixture.CreateDbContext(connectionString: connStr);
+
+        var content1 = await context1.Contents.FindAsync(contentId);
+        var content2 = await context2.Contents.FindAsync(contentId);
+
+        content1!.Body = "Updated by context 1";
+        await context1.SaveChangesAsync();
+
+        content2!.Body = "Updated by context 2";
+        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
+            () => context2.SaveChangesAsync());
+    }
+
+    [Fact]
+    public async Task SequentialUpdates_WithFreshReads_Succeed()
+    {
+        var connStr = _fixture.GetUniqueConnectionString();
+
+        await using var setupContext = _fixture.CreateDbContext(connectionString: connStr);
+        await setupContext.Database.EnsureCreatedAsync();
+
+        var content = TestEntityFactory.CreateContent();
+        setupContext.Contents.Add(content);
+        await setupContext.SaveChangesAsync();
+        var contentId = content.Id;
+
+        await using var context1 = _fixture.CreateDbContext(connectionString: connStr);
+        var c1 = await context1.Contents.FindAsync(contentId);
+        c1!.Body = "First update";
+        await context1.SaveChangesAsync();
+
+        await using var context2 = _fixture.CreateDbContext(connectionString: connStr);
+        var c2 = await context2.Contents.FindAsync(contentId);
+        c2!.Body = "Second update";
+        await context2.SaveChangesAsync();
+
+        await using var verifyContext = _fixture.CreateDbContext(connectionString: connStr);
+        var result = await verifyContext.Contents.FindAsync(contentId);
+        Assert.Equal("Second update", result!.Body);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs
new file mode 100644
index 0000000..8dfa894
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs
@@ -0,0 +1,36 @@
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Persistence;
+
+[Collection("Postgres")]
+public class MigrationTests
+{
+    private readonly PostgresFixture _fixture;
+
+    public MigrationTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+    }
+
+    [Fact]
+    public async Task EnsureCreated_AppliesSchemaCleanly()
+    {
+        var connStr = _fixture.GetUniqueConnectionString();
+        await using var context = _fixture.CreateDbContext(connectionString: connStr);
+
+        await context.Database.EnsureCreatedAsync();
+
+        var tables = await context.Database
+            .SqlQueryRaw<string>(
+                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'")
+            .ToListAsync();
+
+        Assert.Contains("Contents", tables);
+        Assert.Contains("Platforms", tables);
+        Assert.Contains("BrandProfiles", tables);
+        Assert.Contains("ContentCalendarSlots", tables);
+        Assert.Contains("AuditLogEntries", tables);
+        Assert.Contains("Users", tables);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/QueryFilterTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/QueryFilterTests.cs
new file mode 100644
index 0000000..3166b19
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/QueryFilterTests.cs
@@ -0,0 +1,72 @@
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+using PersonalBrandAssistant.Infrastructure.Tests.Utilities;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Persistence;
+
+[Collection("Postgres")]
+public class QueryFilterTests
+{
+    private readonly PostgresFixture _fixture;
+
+    public QueryFilterTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+    }
+
+    [Fact]
+    public async Task QueryContent_ExcludesArchivedByDefault()
+    {
+        var connStr = _fixture.GetUniqueConnectionString();
+
+        await using var setupContext = _fixture.CreateDbContext(connectionString: connStr);
+        await setupContext.Database.EnsureCreatedAsync();
+
+        var active = TestEntityFactory.CreateContent(body: "Active content");
+        var archived = TestEntityFactory.CreateContent(body: "Archived content");
+        archived.TransitionTo(ContentStatus.Review);
+        archived.TransitionTo(ContentStatus.Approved);
+        archived.TransitionTo(ContentStatus.Scheduled);
+        archived.TransitionTo(ContentStatus.Publishing);
+        archived.TransitionTo(ContentStatus.Published);
+        archived.TransitionTo(ContentStatus.Archived);
+
+        setupContext.Contents.AddRange(active, archived);
+        await setupContext.SaveChangesAsync();
+
+        await using var queryContext = _fixture.CreateDbContext(connectionString: connStr);
+        var results = await queryContext.Contents.ToListAsync();
+
+        Assert.Single(results);
+        Assert.Equal("Active content", results[0].Body);
+    }
+
+    [Fact]
+    public async Task IgnoreQueryFilters_IncludesArchivedContent()
+    {
+        var connStr = _fixture.GetUniqueConnectionString();
+
+        await using var setupContext = _fixture.CreateDbContext(connectionString: connStr);
+        await setupContext.Database.EnsureCreatedAsync();
+
+        var active = TestEntityFactory.CreateContent(body: "Active");
+        var archived = TestEntityFactory.CreateContent(body: "Archived");
+        archived.TransitionTo(ContentStatus.Review);
+        archived.TransitionTo(ContentStatus.Approved);
+        archived.TransitionTo(ContentStatus.Scheduled);
+        archived.TransitionTo(ContentStatus.Publishing);
+        archived.TransitionTo(ContentStatus.Published);
+        archived.TransitionTo(ContentStatus.Archived);
+
+        setupContext.Contents.AddRange(active, archived);
+        await setupContext.SaveChangesAsync();
+
+        await using var queryContext = _fixture.CreateDbContext(connectionString: connStr);
+        var results = await queryContext.Contents
+            .IgnoreQueryFilters()
+            .ToListAsync();
+
+        Assert.Equal(2, results.Count);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AuditLogCleanupServiceTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AuditLogCleanupServiceTests.cs
new file mode 100644
index 0000000..71ad134
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AuditLogCleanupServiceTests.cs
@@ -0,0 +1,76 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Infrastructure.Services;
+using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
+
+[Collection("Postgres")]
+public class AuditLogCleanupServiceTests
+{
+    private readonly PostgresFixture _fixture;
+
+    public AuditLogCleanupServiceTests(PostgresFixture fixture)
+    {
+        _fixture = fixture;
+    }
+
+    [Fact]
+    public async Task Cleanup_DeletesEntriesOlderThanRetention()
+    {
+        var connStr = _fixture.GetUniqueConnectionString();
+        await using var context = _fixture.CreateDbContext(connectionString: connStr);
+        await context.Database.EnsureCreatedAsync();
+
+        var now = DateTimeOffset.UtcNow;
+
+        var oldEntry = new AuditLogEntry
+        {
+            EntityType = "Content",
+            EntityId = Guid.NewGuid(),
+            Action = "Updated",
+            Timestamp = now.AddDays(-100),
+        };
+
+        var recentEntry = new AuditLogEntry
+        {
+            EntityType = "Content",
+            EntityId = Guid.NewGuid(),
+            Action = "Created",
+            Timestamp = now.AddDays(-10),
+        };
+
+        context.AuditLogEntries.AddRange(oldEntry, recentEntry);
+        await context.SaveChangesAsync();
+
+        var cutoff = now.AddDays(-90);
+        var deleted = await context.AuditLogEntries
+            .Where(e => e.Timestamp < cutoff)
+            .ExecuteDeleteAsync();
+
+        Assert.Equal(1, deleted);
+
+        var remaining = await context.AuditLogEntries.ToListAsync();
+        Assert.Single(remaining);
+        Assert.Equal("Created", remaining[0].Action);
+    }
+
+    [Fact]
+    public async Task Cleanup_EmptyTable_NoErrors()
+    {
+        var connStr = _fixture.GetUniqueConnectionString();
+        await using var context = _fixture.CreateDbContext(connectionString: connStr);
+        await context.Database.EnsureCreatedAsync();
+
+        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
+        var deleted = await context.AuditLogEntries
+            .Where(e => e.Timestamp < cutoff)
+            .ExecuteDeleteAsync();
+
+        Assert.Equal(0, deleted);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs
new file mode 100644
index 0000000..336f634
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs
@@ -0,0 +1,47 @@
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Utilities;
+
+public static class TestEntityFactory
+{
+    public static Content CreateContent(
+        ContentType type = ContentType.BlogPost,
+        string body = "Test content body",
+        string? title = "Test Title",
+        PlatformType[]? targetPlatforms = null) =>
+        Content.Create(type, body, title, targetPlatforms);
+
+    public static Platform CreatePlatform(
+        PlatformType type = PlatformType.TwitterX,
+        string displayName = "Test Platform",
+        byte[]? accessToken = null,
+        byte[]? refreshToken = null) =>
+        new()
+        {
+            Type = type,
+            DisplayName = displayName,
+            EncryptedAccessToken = accessToken ?? [1, 2, 3],
+            EncryptedRefreshToken = refreshToken ?? [4, 5, 6],
+        };
+
+    public static BrandProfile CreateBrandProfile(
+        string name = "Test Brand",
+        string personaDescription = "Test persona") =>
+        new()
+        {
+            Name = name,
+            PersonaDescription = personaDescription,
+        };
+
+    public static User CreateUser(
+        string displayName = "Test User",
+        string email = "test@example.com",
+        string timeZoneId = "America/New_York") =>
+        new()
+        {
+            DisplayName = displayName,
+            Email = email,
+            TimeZoneId = timeZoneId,
+        };
+}
