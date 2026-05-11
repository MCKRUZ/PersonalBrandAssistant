diff --git a/src/PBA.Application/Features/Content/Queries/CheckVoice.cs b/src/PBA.Application/Features/Content/Queries/CheckVoice.cs
new file mode 100644
index 0000000..4e2bcd2
--- /dev/null
+++ b/src/PBA.Application/Features/Content/Queries/CheckVoice.cs
@@ -0,0 +1,67 @@
+using System.Text.Json;
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.Content.Dtos;
+using PBA.Domain.Common;
+
+namespace PBA.Application.Features.Content.Queries;
+
+public static class CheckVoice
+{
+    public record Query(Guid ContentId) : IRequest<Result<VoiceCheckDto>>;
+
+    public sealed class Handler(IAppDbContext db, ISidecarClient sidecar) : IRequestHandler<Query, Result<VoiceCheckDto>>
+    {
+        public async Task<Result<VoiceCheckDto>> Handle(Query request, CancellationToken cancellationToken)
+        {
+            var content = await db.Contents.FindAsync([request.ContentId], cancellationToken);
+            if (content is null)
+                return Result<VoiceCheckDto>.NotFound($"Content {request.ContentId} not found");
+
+            var profile = await db.BrandProfiles.FirstOrDefaultAsync(cancellationToken);
+
+            var systemPrompt = BuildSystemPrompt(profile);
+            var userPrompt = BuildUserPrompt(content.Body);
+
+            var response = await sidecar.SendPromptAsync(systemPrompt, userPrompt, cancellationToken);
+            var parsed = JsonDocument.Parse(response);
+            var score = parsed.RootElement.GetProperty("score").GetDecimal();
+            var feedback = parsed.RootElement.GetProperty("feedback").GetString() ?? string.Empty;
+
+            content.VoiceScore = score;
+            await db.SaveChangesAsync(cancellationToken);
+
+            return Result<VoiceCheckDto>.Success(new VoiceCheckDto
+            {
+                Score = score,
+                Feedback = feedback
+            });
+        }
+
+        private static string BuildSystemPrompt(Domain.Entities.BrandProfile? profile)
+        {
+            if (profile is null)
+                return "You are a brand voice analyst. Evaluate how well the content matches the brand's voice.";
+
+            return $"""
+                You are a brand voice analyst. Evaluate how well the content matches this brand profile:
+                Personality: {profile.Personality}
+                Tone: {profile.Tone}
+                Vocabulary to use: {string.Join(", ", profile.Vocabulary)}
+                Words to avoid: {string.Join(", ", profile.AvoidWords)}
+                """;
+        }
+
+        private static string BuildUserPrompt(string body)
+        {
+            return $$"""
+                Analyze this content for brand voice alignment:
+
+                {{body}}
+
+                Respond ONLY with JSON: {"score": <0-100>, "feedback": "<explanation>"}
+                """;
+        }
+    }
+}
diff --git a/src/PBA.Application/Features/Content/Queries/GetContent.cs b/src/PBA.Application/Features/Content/Queries/GetContent.cs
new file mode 100644
index 0000000..60c0e6b
--- /dev/null
+++ b/src/PBA.Application/Features/Content/Queries/GetContent.cs
@@ -0,0 +1,69 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.Content.Dtos;
+using PBA.Domain.Common;
+using ContentEntity = PBA.Domain.Entities.Content;
+
+namespace PBA.Application.Features.Content.Queries;
+
+public static class GetContent
+{
+    public record Query(Guid ContentId) : IRequest<Result<ContentDetailDto>>;
+
+    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<ContentDetailDto>>
+    {
+        public async Task<Result<ContentDetailDto>> Handle(Query request, CancellationToken cancellationToken)
+        {
+            var content = await db.Contents
+                .AsNoTracking()
+                .Include(c => c.CrossPosts)
+                .FirstOrDefaultAsync(c => c.Id == request.ContentId, cancellationToken);
+
+            if (content is null)
+                return Result<ContentDetailDto>.NotFound($"Content {request.ContentId} not found");
+
+            var children = await db.Contents
+                .AsNoTracking()
+                .Where(c => c.ParentContentId == request.ContentId && !c.IsDeleted)
+                .Select(c => new ChildContentDto
+                {
+                    Id = c.Id,
+                    Title = c.Title,
+                    ContentType = c.ContentType,
+                    PrimaryPlatform = c.PrimaryPlatform,
+                    Status = c.Status,
+                    UpdatedAt = c.UpdatedAt
+                })
+                .ToListAsync(cancellationToken);
+
+            return Result<ContentDetailDto>.Success(new ContentDetailDto
+            {
+                Id = content.Id,
+                Title = content.Title,
+                ContentType = content.ContentType,
+                Status = content.Status,
+                PrimaryPlatform = content.PrimaryPlatform,
+                VoiceScore = content.VoiceScore,
+                Tags = content.Tags,
+                CreatedAt = content.CreatedAt,
+                UpdatedAt = content.UpdatedAt,
+                ScheduledAt = content.ScheduledAt,
+                PublishedAt = content.PublishedAt,
+                Body = content.Body,
+                ViralityPrediction = content.ViralityPrediction,
+                SourceIdeaId = content.SourceIdeaId,
+                ParentContentId = content.ParentContentId,
+                PlatformPublishes = content.CrossPosts.Select(cp => new PlatformPublishDto
+                {
+                    Id = cp.Id,
+                    Platform = cp.Platform,
+                    PublishStatus = cp.Status,
+                    PublishedUrl = cp.PublishedUrl,
+                    PublishedAt = cp.PublishedAt
+                }).ToList(),
+                Children = children
+            });
+        }
+    }
+}
diff --git a/src/PBA.Application/Features/Content/Queries/ListContent.cs b/src/PBA.Application/Features/Content/Queries/ListContent.cs
new file mode 100644
index 0000000..498c6d7
--- /dev/null
+++ b/src/PBA.Application/Features/Content/Queries/ListContent.cs
@@ -0,0 +1,85 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Common.Models;
+using PBA.Application.Features.Content.Dtos;
+using PBA.Domain.Common;
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Features.Content.Queries;
+
+public static class ListContent
+{
+    public record Query : IRequest<Result<PagedResult<ContentDto>>>
+    {
+        public int Page { get; init; } = 1;
+        public int PageSize { get; init; } = 20;
+        public ContentStatus? Status { get; init; }
+        public Platform? Platform { get; init; }
+        public ContentType? ContentType { get; init; }
+        public DateTimeOffset? DateFrom { get; init; }
+        public DateTimeOffset? DateTo { get; init; }
+        public string? Search { get; init; }
+    }
+
+    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<PagedResult<ContentDto>>>
+    {
+        public async Task<Result<PagedResult<ContentDto>>> Handle(Query request, CancellationToken cancellationToken)
+        {
+            var query = db.Contents.AsNoTracking().AsQueryable();
+
+            query = query.Where(c => c.ParentContentId == null);
+
+            if (request.Status.HasValue)
+                query = query.Where(c => c.Status == request.Status.Value);
+
+            if (request.Platform.HasValue)
+                query = query.Where(c => c.PrimaryPlatform == request.Platform.Value);
+
+            if (request.ContentType.HasValue)
+                query = query.Where(c => c.ContentType == request.ContentType.Value);
+
+            if (request.DateFrom.HasValue)
+                query = query.Where(c => c.UpdatedAt >= request.DateFrom.Value);
+
+            if (request.DateTo.HasValue)
+                query = query.Where(c => c.UpdatedAt <= request.DateTo.Value);
+
+            if (!string.IsNullOrWhiteSpace(request.Search))
+            {
+                var search = request.Search.ToLower();
+                query = query.Where(c => c.Title.ToLower().Contains(search));
+            }
+
+            var totalCount = await query.CountAsync(cancellationToken);
+
+            var items = await query
+                .OrderByDescending(c => c.UpdatedAt)
+                .Skip((request.Page - 1) * request.PageSize)
+                .Take(request.PageSize)
+                .Select(c => new ContentDto
+                {
+                    Id = c.Id,
+                    Title = c.Title,
+                    ContentType = c.ContentType,
+                    Status = c.Status,
+                    PrimaryPlatform = c.PrimaryPlatform,
+                    VoiceScore = c.VoiceScore,
+                    Tags = c.Tags,
+                    CreatedAt = c.CreatedAt,
+                    UpdatedAt = c.UpdatedAt,
+                    ScheduledAt = c.ScheduledAt,
+                    PublishedAt = c.PublishedAt
+                })
+                .ToListAsync(cancellationToken);
+
+            return new PagedResult<ContentDto>
+            {
+                Items = items,
+                TotalCount = totalCount,
+                Page = request.Page,
+                PageSize = request.PageSize
+            };
+        }
+    }
+}
diff --git a/tests/PBA.Application.Tests/Features/Content/Queries/CheckVoiceHandlerTests.cs b/tests/PBA.Application.Tests/Features/Content/Queries/CheckVoiceHandlerTests.cs
new file mode 100644
index 0000000..a4e312d
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/Content/Queries/CheckVoiceHandlerTests.cs
@@ -0,0 +1,115 @@
+using Microsoft.EntityFrameworkCore;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.Content.Queries;
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Xunit;
+using ContentEntity = PBA.Domain.Entities.Content;
+
+namespace PBA.Application.Tests.Features.Content.Queries;
+
+public class CheckVoiceHandlerTests
+{
+    private readonly Mock<ISidecarClient> _sidecarMock = new();
+
+    private static ApplicationDbContext CreateContext()
+    {
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
+            .Options;
+        return new ApplicationDbContext(options);
+    }
+
+    [Fact]
+    public async Task Handle_ValidContent_ReturnsScoreAndFeedback()
+    {
+        await using var context = CreateContext();
+        var content = new ContentEntity { Title = "Test", Body = "Some body text" };
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        _sidecarMock
+            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync("{\"score\": 85, \"feedback\": \"Good match\"}");
+
+        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
+        var result = await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(85m, result.Value!.Score);
+        Assert.Equal("Good match", result.Value.Feedback);
+    }
+
+    [Fact]
+    public async Task Handle_ValidContent_UpdatesVoiceScoreOnEntity()
+    {
+        await using var context = CreateContext();
+        var content = new ContentEntity { Title = "Test", Body = "Some body text" };
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        _sidecarMock
+            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync("{\"score\": 85, \"feedback\": \"Good match\"}");
+
+        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
+        await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);
+
+        var reloaded = await context.Contents.FindAsync(content.Id);
+        Assert.Equal(85m, reloaded!.VoiceScore);
+    }
+
+    [Fact]
+    public async Task Handle_NonExistentContent_ReturnsNotFound()
+    {
+        await using var context = CreateContext();
+
+        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
+        var result = await handler.Handle(new CheckVoice.Query(Guid.NewGuid()), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
+    }
+
+    [Fact]
+    public async Task Handle_MissingBrandProfile_UsesDefaults()
+    {
+        await using var context = CreateContext();
+        var content = new ContentEntity { Title = "Test", Body = "Some body" };
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        _sidecarMock
+            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync("{\"score\": 50, \"feedback\": \"Default profile\"}");
+
+        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
+        var result = await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task Handle_SidecarPrompt_ContainsStructuredJsonInstruction()
+    {
+        await using var context = CreateContext();
+        var content = new ContentEntity { Title = "Test", Body = "Some body" };
+        context.Contents.Add(content);
+        await context.SaveChangesAsync();
+
+        string? capturedUserPrompt = null;
+        _sidecarMock
+            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .Callback<string, string, CancellationToken>((_, userPrompt, _) => capturedUserPrompt = userPrompt)
+            .ReturnsAsync("{\"score\": 75, \"feedback\": \"OK\"}");
+
+        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
+        await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);
+
+        Assert.NotNull(capturedUserPrompt);
+        Assert.Contains("Respond ONLY with JSON", capturedUserPrompt);
+    }
+}
diff --git a/tests/PBA.Application.Tests/Features/Content/Queries/GetContentHandlerTests.cs b/tests/PBA.Application.Tests/Features/Content/Queries/GetContentHandlerTests.cs
new file mode 100644
index 0000000..a2b99cf
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/Content/Queries/GetContentHandlerTests.cs
@@ -0,0 +1,115 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Features.Content.Queries;
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Xunit;
+using ContentEntity = PBA.Domain.Entities.Content;
+
+namespace PBA.Application.Tests.Features.Content.Queries;
+
+public class GetContentHandlerTests
+{
+    private static ApplicationDbContext CreateContext()
+    {
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
+            .Options;
+        return new ApplicationDbContext(options);
+    }
+
+    [Fact]
+    public async Task Handle_ExistingContent_ReturnsDetailWithPlatformPublishes()
+    {
+        await using var context = CreateContext();
+        var content = new ContentEntity { Title = "Test" };
+        context.Contents.Add(content);
+        context.ContentPlatformPublishes.Add(new ContentPlatformPublish
+        {
+            ContentId = content.Id,
+            Platform = Platform.LinkedIn,
+            Status = PublishStatus.Published,
+            PublishedUrl = "https://linkedin.com/post/1"
+        });
+        context.ContentPlatformPublishes.Add(new ContentPlatformPublish
+        {
+            ContentId = content.Id,
+            Platform = Platform.Twitter,
+            Status = PublishStatus.Pending
+        });
+        await context.SaveChangesAsync();
+
+        var handler = new GetContent.Handler(context);
+        var result = await handler.Handle(new GetContent.Query(content.Id), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, result.Value!.PlatformPublishes.Count);
+    }
+
+    [Fact]
+    public async Task Handle_ExistingContent_ReturnsDetailWithChildren()
+    {
+        await using var context = CreateContext();
+        var parent = new ContentEntity { Title = "Parent Post" };
+        context.Contents.Add(parent);
+        context.Contents.Add(new ContentEntity
+        {
+            Title = "LinkedIn Version",
+            ParentContentId = parent.Id,
+            PrimaryPlatform = Platform.LinkedIn
+        });
+        context.Contents.Add(new ContentEntity
+        {
+            Title = "Twitter Version",
+            ParentContentId = parent.Id,
+            PrimaryPlatform = Platform.Twitter
+        });
+        await context.SaveChangesAsync();
+
+        var handler = new GetContent.Handler(context);
+        var result = await handler.Handle(new GetContent.Query(parent.Id), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, result.Value!.Children.Count);
+    }
+
+    [Fact]
+    public async Task Handle_NonExistentId_ReturnsNotFound()
+    {
+        await using var context = CreateContext();
+
+        var handler = new GetContent.Handler(context);
+        var result = await handler.Handle(new GetContent.Query(Guid.NewGuid()), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
+    }
+
+    [Fact]
+    public async Task Handle_ExcludesSoftDeletedChildren()
+    {
+        await using var context = CreateContext();
+        var parent = new ContentEntity { Title = "Parent" };
+        context.Contents.Add(parent);
+        context.Contents.Add(new ContentEntity
+        {
+            Title = "Active Child",
+            ParentContentId = parent.Id
+        });
+        context.Contents.Add(new ContentEntity
+        {
+            Title = "Deleted Child",
+            ParentContentId = parent.Id,
+            IsDeleted = true
+        });
+        await context.SaveChangesAsync();
+
+        var handler = new GetContent.Handler(context);
+        var result = await handler.Handle(new GetContent.Query(parent.Id), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!.Children);
+        Assert.Equal("Active Child", result.Value.Children[0].Title);
+    }
+}
diff --git a/tests/PBA.Application.Tests/Features/Content/Queries/ListContentHandlerTests.cs b/tests/PBA.Application.Tests/Features/Content/Queries/ListContentHandlerTests.cs
new file mode 100644
index 0000000..b66e984
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/Content/Queries/ListContentHandlerTests.cs
@@ -0,0 +1,196 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Features.Content.Queries;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Xunit;
+using ContentEntity = PBA.Domain.Entities.Content;
+
+namespace PBA.Application.Tests.Features.Content.Queries;
+
+public class ListContentHandlerTests
+{
+    private static ApplicationDbContext CreateContext()
+    {
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
+            .Options;
+        return new ApplicationDbContext(options);
+    }
+
+    private static ContentEntity CreateContent(
+        string title = "Test Content",
+        ContentStatus status = ContentStatus.Draft,
+        Platform platform = Platform.Blog,
+        ContentType contentType = ContentType.BlogPost,
+        Guid? parentContentId = null,
+        bool isDeleted = false,
+        DateTimeOffset? updatedAt = null)
+    {
+        return new ContentEntity
+        {
+            Title = title,
+            Status = status,
+            PrimaryPlatform = platform,
+            ContentType = contentType,
+            ParentContentId = parentContentId,
+            IsDeleted = isDeleted,
+            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow
+        };
+    }
+
+    [Fact]
+    public async Task Handle_DefaultQuery_ReturnsPaginatedResults()
+    {
+        await using var context = CreateContext();
+        for (var i = 0; i < 25; i++)
+            context.Contents.Add(CreateContent(title: $"Content {i}"));
+        await context.SaveChangesAsync();
+
+        var handler = new ListContent.Handler(context);
+        var result = await handler.Handle(new ListContent.Query(), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(20, result.Value!.Items.Count);
+        Assert.Equal(25, result.Value.TotalCount);
+        Assert.Equal(2, result.Value.TotalPages);
+    }
+
+    [Fact]
+    public async Task Handle_StatusFilter_ReturnsMatchingOnly()
+    {
+        await using var context = CreateContext();
+        context.Contents.Add(CreateContent(status: ContentStatus.Draft));
+        context.Contents.Add(CreateContent(status: ContentStatus.Draft));
+        context.Contents.Add(CreateContent(status: ContentStatus.Published));
+        await context.SaveChangesAsync();
+
+        var handler = new ListContent.Handler(context);
+        var result = await handler.Handle(new ListContent.Query { Status = ContentStatus.Draft }, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, result.Value!.Items.Count);
+        Assert.All(result.Value.Items, i => Assert.Equal(ContentStatus.Draft, i.Status));
+    }
+
+    [Fact]
+    public async Task Handle_PlatformFilter_ReturnsMatchingOnly()
+    {
+        await using var context = CreateContext();
+        context.Contents.Add(CreateContent(platform: Platform.Blog));
+        context.Contents.Add(CreateContent(platform: Platform.LinkedIn));
+        await context.SaveChangesAsync();
+
+        var handler = new ListContent.Handler(context);
+        var result = await handler.Handle(new ListContent.Query { Platform = Platform.Blog }, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!.Items);
+        Assert.Equal(Platform.Blog, result.Value.Items[0].PrimaryPlatform);
+    }
+
+    [Fact]
+    public async Task Handle_ContentTypeFilter_ReturnsMatchingOnly()
+    {
+        await using var context = CreateContext();
+        context.Contents.Add(CreateContent(contentType: ContentType.BlogPost));
+        context.Contents.Add(CreateContent(contentType: ContentType.Tweet));
+        await context.SaveChangesAsync();
+
+        var handler = new ListContent.Handler(context);
+        var result = await handler.Handle(new ListContent.Query { ContentType = ContentType.BlogPost }, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!.Items);
+        Assert.Equal(ContentType.BlogPost, result.Value.Items[0].ContentType);
+    }
+
+    [Fact]
+    public async Task Handle_DateRangeFilter_ReturnsWithinRange()
+    {
+        await using var context = CreateContext();
+        var now = DateTimeOffset.UtcNow;
+        context.Contents.Add(CreateContent(title: "Old", updatedAt: now.AddDays(-10)));
+        context.Contents.Add(CreateContent(title: "InRange", updatedAt: now.AddDays(-3)));
+        context.Contents.Add(CreateContent(title: "Future", updatedAt: now.AddDays(5)));
+        await context.SaveChangesAsync();
+
+        var handler = new ListContent.Handler(context);
+        var result = await handler.Handle(new ListContent.Query
+        {
+            DateFrom = now.AddDays(-5),
+            DateTo = now.AddDays(-1)
+        }, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!.Items);
+        Assert.Equal("InRange", result.Value.Items[0].Title);
+    }
+
+    [Fact]
+    public async Task Handle_SearchText_MatchesTitleCaseInsensitive()
+    {
+        await using var context = CreateContext();
+        context.Contents.Add(CreateContent(title: "Using Claude for AI"));
+        context.Contents.Add(CreateContent(title: "Using claude efficiently"));
+        context.Contents.Add(CreateContent(title: "Other topic"));
+        await context.SaveChangesAsync();
+
+        var handler = new ListContent.Handler(context);
+        var result = await handler.Handle(new ListContent.Query { Search = "claude" }, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, result.Value!.Items.Count);
+    }
+
+    [Fact]
+    public async Task Handle_ExcludesChildContent()
+    {
+        await using var context = CreateContext();
+        var parent = CreateContent(title: "Parent");
+        context.Contents.Add(parent);
+        await context.SaveChangesAsync();
+
+        context.Contents.Add(CreateContent(title: "Child", parentContentId: parent.Id));
+        await context.SaveChangesAsync();
+
+        var handler = new ListContent.Handler(context);
+        var result = await handler.Handle(new ListContent.Query(), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!.Items);
+        Assert.Equal("Parent", result.Value.Items[0].Title);
+    }
+
+    [Fact]
+    public async Task Handle_ExcludesSoftDeletedContent()
+    {
+        await using var context = CreateContext();
+        context.Contents.Add(CreateContent(title: "Active"));
+        context.Contents.Add(CreateContent(title: "Deleted", isDeleted: true));
+        await context.SaveChangesAsync();
+
+        var handler = new ListContent.Handler(context);
+        var result = await handler.Handle(new ListContent.Query(), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Single(result.Value!.Items);
+        Assert.Equal("Active", result.Value.Items[0].Title);
+    }
+
+    [Fact]
+    public async Task Handle_OrdersByUpdatedAtDescending()
+    {
+        await using var context = CreateContext();
+        var now = DateTimeOffset.UtcNow;
+        context.Contents.Add(CreateContent(title: "Oldest", updatedAt: now.AddDays(-3)));
+        context.Contents.Add(CreateContent(title: "Newest", updatedAt: now));
+        context.Contents.Add(CreateContent(title: "Middle", updatedAt: now.AddDays(-1)));
+        await context.SaveChangesAsync();
+
+        var handler = new ListContent.Handler(context);
+        var result = await handler.Handle(new ListContent.Query(), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("Newest", result.Value!.Items[0].Title);
+    }
+}
