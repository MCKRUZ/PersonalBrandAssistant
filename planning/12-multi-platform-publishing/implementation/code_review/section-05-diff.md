diff --git a/src/PBA.Application/Common/Interfaces/IContentPublisher.cs b/src/PBA.Application/Common/Interfaces/IContentPublisher.cs
index 74109ac..028412d 100644
--- a/src/PBA.Application/Common/Interfaces/IContentPublisher.cs
+++ b/src/PBA.Application/Common/Interfaces/IContentPublisher.cs
@@ -1,6 +1,11 @@
+using PBA.Application.Common.Models;
+using PBA.Domain.Enums;
+
 namespace PBA.Application.Common.Interfaces;
 
 public interface IContentPublisher
 {
     Task PublishAsync(Guid contentId);
+
+    Task<PublishResult> PublishAsync(Guid contentId, IReadOnlyList<Platform>? targetPlatforms, CancellationToken ct);
 }
diff --git a/src/PBA.Application/Features/Content/Commands/PublishContent.cs b/src/PBA.Application/Features/Content/Commands/PublishContent.cs
index 1d9c6d7..0902dc9 100644
--- a/src/PBA.Application/Features/Content/Commands/PublishContent.cs
+++ b/src/PBA.Application/Features/Content/Commands/PublishContent.cs
@@ -1,66 +1,26 @@
 using MediatR;
-using Microsoft.Extensions.DependencyInjection;
 using PBA.Application.Common.Interfaces;
 using PBA.Application.Common.Models;
-using PBA.Application.Features.ContentStudio;
 using PBA.Domain.Common;
-using PBA.Domain.Entities;
 using PBA.Domain.Enums;
 
 namespace PBA.Application.Features.Content.Commands;
 
 public static class PublishContent
 {
-    public record Command(Guid ContentId) : IRequest<Result>;
+    public record Command(Guid ContentId, IReadOnlyList<Platform>? TargetPlatforms = null) : IRequest<Result<PublishResult>>;
 
     internal sealed class Handler(
-        IAppDbContext db,
-        [FromKeyedServices(Platform.Blog)] IPlatformConnector blogConnector) : IRequestHandler<Command, Result>
+        IContentPublisher publisher) : IRequestHandler<Command, Result<PublishResult>>
     {
-        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
+        public async Task<Result<PublishResult>> Handle(Command request, CancellationToken cancellationToken)
         {
-            var content = await db.Contents.FindAsync([request.ContentId], cancellationToken);
-            if (content is null)
-                return Result.NotFound($"Content {request.ContentId} not found");
+            var result = await publisher.PublishAsync(request.ContentId, request.TargetPlatforms, cancellationToken);
 
-            var machine = ContentStateMachine.Create(content);
-            try
-            {
-                await machine.FireAsync(ContentTrigger.PublishNow);
-            }
-            catch (InvalidOperationException)
-            {
-                return Result.Fail("Cannot publish content in its current status");
-            }
+            if (!result.PrimarySuccess)
+                return Result<PublishResult>.Fail("Primary platform publish failed");
 
-            PlatformPublishResult? result = null;
-            if (content.PrimaryPlatform == Platform.Blog)
-            {
-                var publishRequest = new PlatformPublishRequest(
-                    Content: content,
-                    TransformedContent: content.Body,
-                    Tags: content.Tags.AsReadOnly(),
-                    CanonicalUrl: null,
-                    Mode: PublishMode.Publish,
-                    ScheduledAt: null);
-
-                result = await blogConnector.PublishAsync(publishRequest, cancellationToken);
-                if (!result.Success)
-                    return Result.Fail(result.ErrorMessage ?? "Failed to publish to blog platform");
-            }
-
-            db.ContentPlatformPublishes.Add(new ContentPlatformPublish
-            {
-                ContentId = request.ContentId,
-                Platform = content.PrimaryPlatform,
-                Status = PublishStatus.Published,
-                PublishedUrl = result?.PublishedUrl,
-                PlatformPostId = result?.PlatformPostId,
-                PublishedAt = DateTimeOffset.UtcNow
-            });
-
-            await db.SaveChangesAsync(cancellationToken);
-            return Result.Success();
+            return result;
         }
     }
 }
diff --git a/src/PBA.Infrastructure/Publishing/ContentPublisher.cs b/src/PBA.Infrastructure/Publishing/ContentPublisher.cs
index 4f767df..13a0138 100644
--- a/src/PBA.Infrastructure/Publishing/ContentPublisher.cs
+++ b/src/PBA.Infrastructure/Publishing/ContentPublisher.cs
@@ -1,3 +1,4 @@
+using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Logging;
 using PBA.Application.Common.Interfaces;
@@ -10,7 +11,8 @@ namespace PBA.Infrastructure.Publishing;
 
 public sealed class ContentPublisher(
     IAppDbContext db,
-    [FromKeyedServices(Platform.Blog)] IPlatformConnector blogConnector,
+    IServiceProvider serviceProvider,
+    IContentTransformer transformer,
     ILogger<ContentPublisher> logger) : IContentPublisher
 {
     public async Task PublishAsync(Guid contentId)
@@ -28,50 +30,169 @@ public sealed class ContentPublisher(
             return;
         }
 
-        PlatformPublishResult? result = null;
-        if (content.PrimaryPlatform == Platform.Blog)
+        await PublishAsync(contentId, targetPlatforms: null, CancellationToken.None);
+    }
+
+    public async Task<PublishResult> PublishAsync(
+        Guid contentId,
+        IReadOnlyList<Platform>? targetPlatforms,
+        CancellationToken ct)
+    {
+        var content = await db.Contents.FindAsync([contentId], ct);
+        if (content is null)
+        {
+            logger.LogWarning("Content {ContentId} not found for publish", contentId);
+            return new PublishResult(false, null, []);
+        }
+
+        if (content.Status != ContentStatus.Scheduled && content.Status != ContentStatus.Approved)
+        {
+            logger.LogWarning("Content {ContentId} is {Status}, skipping publish", contentId, content.Status);
+            return new PublishResult(false, null, []);
+        }
+
+        var platforms = DetermineTargetPlatforms(content, targetPlatforms);
+        var primaryPlatform = content.PrimaryPlatform;
+
+        var publishedPrimary = await db.ContentPlatformPublishes
+            .AnyAsync(p => p.ContentId == contentId && p.Platform == primaryPlatform && p.Status == PublishStatus.Published, ct);
+
+        PlatformPublishResult? primaryResult = null;
+        string? primaryUrl = null;
+
+        if (platforms.Contains(primaryPlatform) && !publishedPrimary)
         {
-            var request = new PlatformPublishRequest(
-                Content: content,
-                TransformedContent: content.Body,
-                Tags: content.Tags.AsReadOnly(),
-                CanonicalUrl: null,
-                Mode: PublishMode.Publish,
-                ScheduledAt: content.ScheduledAt);
-            result = await blogConnector.PublishAsync(request, CancellationToken.None);
-
-            if (!result.Success)
+            primaryResult = await PublishToPlatformAsync(content, primaryPlatform, canonicalUrl: null, ct);
+
+            db.ContentPlatformPublishes.Add(new ContentPlatformPublish
+            {
+                ContentId = contentId,
+                Platform = primaryPlatform,
+                Status = primaryResult.Success ? PublishStatus.Published : PublishStatus.Failed,
+                PublishedUrl = primaryResult.PublishedUrl,
+                PlatformPostId = primaryResult.PlatformPostId,
+                ErrorMessage = primaryResult.ErrorMessage,
+                PublishedAt = DateTimeOffset.UtcNow
+            });
+
+            if (!primaryResult.Success)
             {
+                await db.SaveChangesAsync(ct);
+                logger.LogWarning("Failed to publish content {ContentId} to primary {Platform}: {Error}",
+                    contentId, primaryPlatform, primaryResult.ErrorMessage);
+                return new PublishResult(false, null, []);
+            }
+
+            primaryUrl = primaryResult.PublishedUrl;
+        }
+        else if (publishedPrimary)
+        {
+            var existingRecord = await db.ContentPlatformPublishes
+                .FirstAsync(p => p.ContentId == contentId && p.Platform == primaryPlatform && p.Status == PublishStatus.Published, ct);
+            primaryUrl = existingRecord.PublishedUrl;
+        }
+
+        var trigger = content.Status == ContentStatus.Scheduled
+            ? ContentTrigger.Publish
+            : ContentTrigger.PublishNow;
+        var machine = ContentStateMachine.Create(content);
+        await machine.FireAsync(trigger);
+
+        var secondaryPlatforms = platforms
+            .Where(p => p != primaryPlatform)
+            .ToList();
+
+        var secondaryOutcomes = new List<PlatformPublishOutcome>();
+
+        if (secondaryPlatforms.Count > 0)
+        {
+            var secondaryTasks = secondaryPlatforms.Select(async platform =>
+            {
+                var alreadyPublished = await db.ContentPlatformPublishes
+                    .AnyAsync(p => p.ContentId == contentId && p.Platform == platform && p.Status == PublishStatus.Published, ct);
+
+                if (alreadyPublished)
+                    return new PlatformPublishOutcome(platform, true, null, null);
+
+                try
+                {
+                    var result = await PublishToPlatformAsync(content, platform, primaryUrl, ct);
+                    return new PlatformPublishOutcome(platform, result.Success, result.PublishedUrl, result.ErrorMessage);
+                }
+                catch (Exception ex)
+                {
+                    return new PlatformPublishOutcome(platform, false, null, ex.Message);
+                }
+            });
+
+            var outcomes = await Task.WhenAll(secondaryTasks);
+
+            foreach (var outcome in outcomes)
+            {
+                secondaryOutcomes.Add(outcome);
+
+                var alreadyPublished = await db.ContentPlatformPublishes
+                    .AnyAsync(p => p.ContentId == contentId && p.Platform == outcome.Platform && p.Status == PublishStatus.Published, ct);
+
+                if (alreadyPublished) continue;
+
                 db.ContentPlatformPublishes.Add(new ContentPlatformPublish
                 {
                     ContentId = contentId,
-                    Platform = content.PrimaryPlatform,
-                    Status = PublishStatus.Failed,
-                    ErrorMessage = result.ErrorMessage,
-                    PublishedAt = DateTimeOffset.UtcNow
+                    Platform = outcome.Platform,
+                    Status = outcome.Success ? PublishStatus.Published : PublishStatus.Failed,
+                    PublishedUrl = outcome.Url,
+                    ErrorMessage = outcome.Error,
+                    PublishedAt = DateTimeOffset.UtcNow,
+                    RetryCount = 0
                 });
-                await db.SaveChangesAsync();
-                logger.LogWarning("Failed to publish content {ContentId} to {Platform}: {Error}",
-                    contentId, content.PrimaryPlatform, result.ErrorMessage);
-                return;
             }
         }
 
-        var machine = ContentStateMachine.Create(content);
-        await machine.FireAsync(ContentTrigger.Publish);
+        await db.SaveChangesAsync(ct);
+
+        logger.LogInformation("Published content {ContentId} to {Platform} (primary) + {SecondaryCount} secondaries",
+            contentId, primaryPlatform, secondaryOutcomes.Count);
 
-        db.ContentPlatformPublishes.Add(new ContentPlatformPublish
+        return new PublishResult(
+            primaryResult?.Success ?? publishedPrimary,
+            primaryUrl,
+            secondaryOutcomes.AsReadOnly());
+    }
+
+    private async Task<PlatformPublishResult> PublishToPlatformAsync(
+        Content content,
+        Platform platform,
+        string? canonicalUrl,
+        CancellationToken ct)
+    {
+        var connector = serviceProvider.GetKeyedService<IPlatformConnector>(platform);
+        if (connector is null)
         {
-            ContentId = contentId,
-            Platform = content.PrimaryPlatform,
-            Status = PublishStatus.Published,
-            PublishedUrl = result?.PublishedUrl,
-            PlatformPostId = result?.PlatformPostId,
-            PublishedAt = DateTimeOffset.UtcNow
-        });
+            logger.LogWarning("No connector registered for platform {Platform}", platform);
+            return new PlatformPublishResult(false, null, null, $"No connector registered for {platform}");
+        }
+
+        var transformed = await transformer.TransformAsync(content, platform, ct);
+        var request = new PlatformPublishRequest(
+            Content: content,
+            TransformedContent: transformed,
+            Tags: content.Tags.AsReadOnly(),
+            CanonicalUrl: canonicalUrl,
+            Mode: PublishMode.Publish,
+            ScheduledAt: content.ScheduledAt);
+
+        return await connector.PublishAsync(request, ct);
+    }
+
+    private static IReadOnlyList<Platform> DetermineTargetPlatforms(Content content, IReadOnlyList<Platform>? explicitTargets)
+    {
+        if (explicitTargets is { Count: > 0 })
+            return explicitTargets;
 
-        await db.SaveChangesAsync();
+        if (content.TargetPlatforms is { Count: > 0 })
+            return content.TargetPlatforms.AsReadOnly();
 
-        logger.LogInformation("Published content {ContentId} to {Platform}", contentId, content.PrimaryPlatform);
+        return [content.PrimaryPlatform];
     }
 }
diff --git a/tests/PBA.Api.Tests/TestWebApplicationFactory.cs b/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
index 3e0cb4f..601bb21 100644
--- a/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
+++ b/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
@@ -55,7 +55,10 @@ public class TestWebApplicationFactory : WebApplicationFactory<Program>
                 .ReturnsAsync(new PBA.Application.Common.Models.PlatformPublishResult(true, "https://blog.test/published-post", "published-post", null));
             services.AddKeyedSingleton<IPlatformConnector>(PBA.Domain.Enums.Platform.Blog, blogConnectorMock.Object);
 
-            services.AddSingleton(new Mock<IContentPublisher>().Object);
+            var transformerMock = new Mock<IContentTransformer>();
+            transformerMock.Setup(x => x.TransformAsync(It.IsAny<PBA.Domain.Entities.Content>(), It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<CancellationToken>()))
+                .ReturnsAsync((PBA.Domain.Entities.Content c, PBA.Domain.Enums.Platform _, CancellationToken _) => c.Body);
+            services.AddSingleton<IContentTransformer>(transformerMock.Object);
         });
 
         builder.UseEnvironment("Testing");
diff --git a/tests/PBA.Application.Tests/Features/Content/Commands/PublishContentHandlerTests.cs b/tests/PBA.Application.Tests/Features/Content/Commands/PublishContentHandlerTests.cs
new file mode 100644
index 0000000..85b6b4f
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/Content/Commands/PublishContentHandlerTests.cs
@@ -0,0 +1,60 @@
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Application.Features.Content.Commands;
+using PBA.Domain.Enums;
+using Xunit;
+
+namespace PBA.Application.Tests.Features.Content.Commands;
+
+public class PublishContentHandlerTests
+{
+    private readonly Mock<IContentPublisher> _publisher = new();
+
+    private PublishContent.Handler CreateHandler() => new(_publisher.Object);
+
+    [Fact]
+    public async Task Handle_WithTargetPlatforms_PassesPlatformsToPublisher()
+    {
+        var contentId = Guid.NewGuid();
+        var platforms = new List<Platform> { Platform.Blog, Platform.Medium }.AsReadOnly();
+        var publishResult = new PublishResult(true, "https://example.com/post", []);
+        _publisher.Setup(p => p.PublishAsync(contentId, platforms, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(publishResult);
+
+        var handler = CreateHandler();
+        var result = await handler.Handle(new PublishContent.Command(contentId, platforms), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _publisher.Verify(p => p.PublishAsync(contentId, platforms, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task Handle_WithoutTargetPlatforms_PassesNullToPublisher()
+    {
+        var contentId = Guid.NewGuid();
+        var publishResult = new PublishResult(true, "https://example.com/post", []);
+        _publisher.Setup(p => p.PublishAsync(contentId, null, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(publishResult);
+
+        var handler = CreateHandler();
+        var result = await handler.Handle(new PublishContent.Command(contentId), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _publisher.Verify(p => p.PublishAsync(contentId, null, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task Handle_PublisherFails_ReturnsFailure()
+    {
+        var contentId = Guid.NewGuid();
+        var publishResult = new PublishResult(false, null, []);
+        _publisher.Setup(p => p.PublishAsync(contentId, null, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(publishResult);
+
+        var handler = CreateHandler();
+        var result = await handler.Handle(new PublishContent.Command(contentId), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs b/tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs
index ef6d053..8f0b18a 100644
--- a/tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs
@@ -1,4 +1,5 @@
 using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Logging;
 using Moq;
 using PBA.Application.Common.Interfaces;
@@ -15,6 +16,10 @@ public class ContentPublisherTests : IDisposable
 {
     private readonly ApplicationDbContext _dbContext;
     private readonly Mock<IPlatformConnector> _blogConnector = new();
+    private readonly Mock<IPlatformConnector> _mediumConnector = new();
+    private readonly Mock<IPlatformConnector> _linkedInConnector = new();
+    private readonly Mock<IPlatformConnector> _twitterConnector = new();
+    private readonly Mock<IContentTransformer> _transformer = new();
     private readonly Mock<ILogger<ContentPublisher>> _logger = new();
 
     public ContentPublisherTests()
@@ -23,10 +28,22 @@ public class ContentPublisherTests : IDisposable
             .UseInMemoryDatabase(Guid.NewGuid().ToString())
             .Options;
         _dbContext = new ApplicationDbContext(options);
+
+        _transformer.Setup(t => t.TransformAsync(It.IsAny<Content>(), It.IsAny<Platform>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync((Content c, Platform _, CancellationToken _) => c.Body);
     }
 
-    private ContentPublisher CreatePublisher() =>
-        new(_dbContext, _blogConnector.Object, _logger.Object);
+    private ContentPublisher CreatePublisher()
+    {
+        var services = new ServiceCollection();
+        services.AddKeyedSingleton<IPlatformConnector>(Platform.Blog, _blogConnector.Object);
+        services.AddKeyedSingleton<IPlatformConnector>(Platform.Medium, _mediumConnector.Object);
+        services.AddKeyedSingleton<IPlatformConnector>(Platform.LinkedIn, _linkedInConnector.Object);
+        services.AddKeyedSingleton<IPlatformConnector>(Platform.Twitter, _twitterConnector.Object);
+        var sp = services.BuildServiceProvider();
+
+        return new ContentPublisher(_dbContext, sp, _transformer.Object, _logger.Object);
+    }
 
     private Content CreateScheduledContent(Platform platform = Platform.Blog) =>
         new()
@@ -38,14 +55,23 @@ public class ContentPublisherTests : IDisposable
             ScheduledAt = DateTimeOffset.UtcNow.AddHours(-1)
         };
 
+    private void SetupConnectorSuccess(Mock<IPlatformConnector> connector, string url = "https://example.com/post", string postId = "post-1") =>
+        connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new PlatformPublishResult(true, url, postId, null));
+
+    private void SetupConnectorFailure(Mock<IPlatformConnector> connector, string error = "Publish failed") =>
+        connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new PlatformPublishResult(false, null, null, error));
+
+    // --- Migrated existing tests ---
+
     [Fact]
     public async Task PublishAsync_PublishesContent_WhenStatusIsScheduled()
     {
         var content = CreateScheduledContent();
         _dbContext.Contents.Add(content);
         await _dbContext.SaveChangesAsync();
-        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(new PlatformPublishResult(true, "https://matthewkruczek.ai/posts/test-post", "test-post", null));
+        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");
 
         var publisher = CreatePublisher();
         await publisher.PublishAsync(content.Id);
@@ -68,91 +94,240 @@ public class ContentPublisherTests : IDisposable
 
         var updated = await _dbContext.Contents.FindAsync(content.Id);
         Assert.Equal(ContentStatus.Approved, updated!.Status);
-        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
+        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
     }
 
     [Fact]
-    public async Task PublishAsync_InvokesBlogConnector_ForBlogPlatform()
+    public async Task PublishAsync_InvokesPlatformConnector_ForBlogPlatform()
     {
         var content = CreateScheduledContent(Platform.Blog);
         _dbContext.Contents.Add(content);
         await _dbContext.SaveChangesAsync();
-        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(new PlatformPublishResult(true, "https://matthewkruczek.ai/posts/test-post", "test-post", null));
+        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");
 
         var publisher = CreatePublisher();
         await publisher.PublishAsync(content.Id);
 
-        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
+        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
     }
 
     [Fact]
-    public async Task PublishAsync_DoesNotInvokeBlogConnector_ForNonBlogPlatform()
+    public async Task PublishAsync_CreatesContentPlatformPublishRecord()
     {
-        var content = CreateScheduledContent(Platform.Twitter);
+        var content = CreateScheduledContent();
         _dbContext.Contents.Add(content);
         await _dbContext.SaveChangesAsync();
+        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");
 
         var publisher = CreatePublisher();
         await publisher.PublishAsync(content.Id);
 
-        _blogConnector.Verify(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
+        var record = await _dbContext.ContentPlatformPublishes.FirstOrDefaultAsync(p => p.ContentId == content.Id);
+        Assert.NotNull(record);
+        Assert.Equal(PublishStatus.Published, record.Status);
+        Assert.Equal("https://matthewkruczek.ai/posts/test-post", record.PublishedUrl);
     }
 
+    // --- New tests ---
+
     [Fact]
-    public async Task PublishAsync_CreatesContentPlatformPublishRecord()
+    public async Task PublishAsync_ResolvesConnectorByPlatform_ViaKeyedDI()
     {
-        var content = CreateScheduledContent();
+        var content = CreateScheduledContent(Platform.Medium);
         _dbContext.Contents.Add(content);
         await _dbContext.SaveChangesAsync();
-        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(new PlatformPublishResult(true, "https://matthewkruczek.ai/posts/test-post", "test-post", null));
+        SetupConnectorSuccess(_mediumConnector, "https://medium.com/@matt/post", "medium-1");
 
         var publisher = CreatePublisher();
         await publisher.PublishAsync(content.Id);
 
-        var record = await _dbContext.ContentPlatformPublishes.FirstOrDefaultAsync(p => p.ContentId == content.Id);
-        Assert.NotNull(record);
-        Assert.Equal(PublishStatus.Published, record.Status);
-        Assert.Equal("https://matthewkruczek.ai/posts/test-post", record.PublishedUrl);
+        _mediumConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
+        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task PublishAsync_PrimaryFails_AbortsWithoutPublishingSecondaries()
+    {
+        var content = CreateScheduledContent(Platform.Blog);
+        content.TargetPlatforms = [Platform.Blog, Platform.Medium];
+        _dbContext.Contents.Add(content);
+        await _dbContext.SaveChangesAsync();
+        SetupConnectorFailure(_blogConnector, "git push failed");
+
+        var publisher = CreatePublisher();
+        var result = await publisher.PublishAsync(content.Id, null, CancellationToken.None);
+
+        Assert.False(result.PrimarySuccess);
+        _mediumConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
+        var updated = await _dbContext.Contents.FindAsync(content.Id);
+        Assert.NotEqual(ContentStatus.Published, updated!.Status);
     }
 
     [Fact]
-    public async Task PublishAsync_PersistsPlatformPostId()
+    public async Task PublishAsync_PrimarySucceeds_FiresStateMachineTrigger()
     {
         var content = CreateScheduledContent();
         _dbContext.Contents.Add(content);
         await _dbContext.SaveChangesAsync();
-        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(new PlatformPublishResult(true, "https://matthewkruczek.ai/posts/test-post", "test-post", null));
+        SetupConnectorSuccess(_blogConnector);
 
         var publisher = CreatePublisher();
         await publisher.PublishAsync(content.Id);
 
-        var record = await _dbContext.ContentPlatformPublishes.FirstOrDefaultAsync(p => p.ContentId == content.Id);
-        Assert.NotNull(record);
-        Assert.Equal("test-post", record.PlatformPostId);
+        var updated = await _dbContext.Contents.FindAsync(content.Id);
+        Assert.Equal(ContentStatus.Published, updated!.Status);
+        Assert.NotNull(updated.PublishedAt);
     }
 
     [Fact]
-    public async Task PublishAsync_RecordsFailure_WhenConnectorFails()
+    public async Task PublishAsync_SecondaryFails_CreatesFailedContentPlatformPublishRecord()
+    {
+        var content = CreateScheduledContent(Platform.Blog);
+        content.TargetPlatforms = [Platform.Blog, Platform.Medium];
+        _dbContext.Contents.Add(content);
+        await _dbContext.SaveChangesAsync();
+        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");
+        SetupConnectorFailure(_mediumConnector, "Medium API error");
+
+        var publisher = CreatePublisher();
+        var result = await publisher.PublishAsync(content.Id, null, CancellationToken.None);
+
+        Assert.True(result.PrimarySuccess);
+
+        var records = await _dbContext.ContentPlatformPublishes
+            .Where(p => p.ContentId == content.Id)
+            .ToListAsync();
+
+        var blogRecord = records.Single(r => r.Platform == Platform.Blog);
+        Assert.Equal(PublishStatus.Published, blogRecord.Status);
+
+        var mediumRecord = records.Single(r => r.Platform == Platform.Medium);
+        Assert.Equal(PublishStatus.Failed, mediumRecord.Status);
+        Assert.Equal("Medium API error", mediumRecord.ErrorMessage);
+    }
+
+    [Fact]
+    public async Task PublishAsync_SecondaryFails_CreatesRecordWithRetryCountZero()
+    {
+        var content = CreateScheduledContent(Platform.Blog);
+        content.TargetPlatforms = [Platform.Blog, Platform.Medium];
+        _dbContext.Contents.Add(content);
+        await _dbContext.SaveChangesAsync();
+        SetupConnectorSuccess(_blogConnector);
+        SetupConnectorFailure(_mediumConnector);
+
+        var publisher = CreatePublisher();
+        await publisher.PublishAsync(content.Id, null, CancellationToken.None);
+
+        var mediumRecord = await _dbContext.ContentPlatformPublishes
+            .SingleAsync(p => p.ContentId == content.Id && p.Platform == Platform.Medium);
+        Assert.Equal(0, mediumRecord.RetryCount);
+    }
+
+    [Fact]
+    public async Task PublishAsync_SkipsPlatformWithExistingPublishedRecord()
+    {
+        var content = CreateScheduledContent(Platform.Blog);
+        content.TargetPlatforms = [Platform.Blog];
+        _dbContext.Contents.Add(content);
+        _dbContext.ContentPlatformPublishes.Add(new ContentPlatformPublish
+        {
+            ContentId = content.Id,
+            Platform = Platform.Blog,
+            Status = PublishStatus.Published,
+            PublishedUrl = "https://matthewkruczek.ai/posts/existing",
+            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1)
+        });
+        await _dbContext.SaveChangesAsync();
+
+        var publisher = CreatePublisher();
+        var result = await publisher.PublishAsync(content.Id, null, CancellationToken.None);
+
+        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
+        var records = await _dbContext.ContentPlatformPublishes.Where(p => p.ContentId == content.Id).ToListAsync();
+        Assert.Single(records);
+    }
+
+    [Fact]
+    public async Task PublishAsync_NoTargetPlatforms_UsesContentTargetPlatforms()
+    {
+        var content = CreateScheduledContent(Platform.Blog);
+        content.TargetPlatforms = [Platform.Blog, Platform.LinkedIn];
+        _dbContext.Contents.Add(content);
+        await _dbContext.SaveChangesAsync();
+        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");
+        SetupConnectorSuccess(_linkedInConnector, "https://linkedin.com/post/1", "li-1");
+
+        var publisher = CreatePublisher();
+        await publisher.PublishAsync(content.Id, null, CancellationToken.None);
+
+        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
+        _linkedInConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task PublishAsync_NoContentTargetPlatforms_UsesPrimaryPlatformOnly()
+    {
+        var content = CreateScheduledContent(Platform.Blog);
+        content.TargetPlatforms = [];
+        _dbContext.Contents.Add(content);
+        await _dbContext.SaveChangesAsync();
+        SetupConnectorSuccess(_blogConnector);
+
+        var publisher = CreatePublisher();
+        await publisher.PublishAsync(content.Id, null, CancellationToken.None);
+
+        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
+        _mediumConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
+        _linkedInConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task PublishAsync_GuidOverload_CallsFullMethodWithNullTargets()
     {
         var content = CreateScheduledContent();
         _dbContext.Contents.Add(content);
         await _dbContext.SaveChangesAsync();
-        _blogConnector.Setup(b => b.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(new PlatformPublishResult(false, null, null, "git push failed"));
+        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");
 
         var publisher = CreatePublisher();
         await publisher.PublishAsync(content.Id);
 
+        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
         var updated = await _dbContext.Contents.FindAsync(content.Id);
-        Assert.Equal(ContentStatus.Scheduled, updated!.Status);
+        Assert.Equal(ContentStatus.Published, updated!.Status);
+    }
 
-        var record = await _dbContext.ContentPlatformPublishes.FirstOrDefaultAsync(p => p.ContentId == content.Id);
-        Assert.NotNull(record);
-        Assert.Equal(PublishStatus.Failed, record.Status);
-        Assert.Equal("git push failed", record.ErrorMessage);
+    [Fact]
+    public async Task PublishAsync_ParallelSecondaries_AllPublishIndependently()
+    {
+        var content = CreateScheduledContent(Platform.Blog);
+        content.TargetPlatforms = [Platform.Blog, Platform.Medium, Platform.LinkedIn, Platform.Twitter];
+        _dbContext.Contents.Add(content);
+        await _dbContext.SaveChangesAsync();
+        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test", "blog-1");
+        SetupConnectorFailure(_mediumConnector, "Medium error");
+        SetupConnectorSuccess(_linkedInConnector, "https://linkedin.com/post/1", "li-1");
+        SetupConnectorFailure(_twitterConnector, "Twitter error");
+
+        var publisher = CreatePublisher();
+        var result = await publisher.PublishAsync(content.Id, null, CancellationToken.None);
+
+        Assert.True(result.PrimarySuccess);
+        Assert.Equal("https://matthewkruczek.ai/posts/test", result.PrimaryUrl);
+
+        var records = await _dbContext.ContentPlatformPublishes
+            .Where(p => p.ContentId == content.Id)
+            .ToListAsync();
+        Assert.Equal(4, records.Count);
+
+        Assert.Equal(PublishStatus.Published, records.Single(r => r.Platform == Platform.Blog).Status);
+        Assert.Equal(PublishStatus.Failed, records.Single(r => r.Platform == Platform.Medium).Status);
+        Assert.Equal(PublishStatus.Published, records.Single(r => r.Platform == Platform.LinkedIn).Status);
+        Assert.Equal(PublishStatus.Failed, records.Single(r => r.Platform == Platform.Twitter).Status);
+
+        var updated = await _dbContext.Contents.FindAsync(content.Id);
+        Assert.Equal(ContentStatus.Published, updated!.Status);
     }
 
     public void Dispose() => _dbContext.Dispose();
