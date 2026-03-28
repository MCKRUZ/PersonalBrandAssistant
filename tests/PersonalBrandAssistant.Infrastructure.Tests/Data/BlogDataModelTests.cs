using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Data;

[Collection("Postgres")]
public class BlogDataModelTests
{
    private readonly PostgresFixture _fixture;

    public BlogDataModelTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<ApplicationDbContext> CreateContextAsync()
    {
        var ctx = _fixture.CreateDbContext();
        await ctx.Database.EnsureCreatedAsync();
        return ctx;
    }

    private static Content CreateContent() =>
        Content.Create(ContentType.BlogPost, "Test blog post body", "Test Title");

    [Fact]
    public async Task ChatConversation_CanBeCreatedAndPersisted_WithJsonMessages()
    {
        await using var ctx = await CreateContextAsync();
        var content = CreateContent();
        ctx.Contents.Add(content);

        var conversation = new ChatConversation
        {
            ContentId = content.Id,
            Messages =
            [
                new("user", "Write about AI", DateTimeOffset.UtcNow),
                new("assistant", "Sure, here's a draft...", DateTimeOffset.UtcNow),
            ],
            LastMessageAt = DateTimeOffset.UtcNow,
        };
        ctx.ChatConversations.Add(conversation);
        await ctx.SaveChangesAsync();

        await using var ctx2 = _fixture.CreateDbContext();
        var loaded = await ctx2.ChatConversations.FirstAsync(c => c.Id == conversation.Id);
        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal("user", loaded.Messages[0].Role);
        Assert.Equal("assistant", loaded.Messages[1].Role);
    }

    [Fact]
    public async Task ChatConversation_Messages_HandlesSpecialCharacters()
    {
        await using var ctx = await CreateContextAsync();
        var content = CreateContent();
        ctx.Contents.Add(content);

        var specialContent = "```csharp\nvar x = \"hello \\n world\";\n``` with 'quotes' & <html> and emojis 🎉";
        var conversation = new ChatConversation
        {
            ContentId = content.Id,
            Messages = [new("user", specialContent, DateTimeOffset.UtcNow)],
            LastMessageAt = DateTimeOffset.UtcNow,
        };
        ctx.ChatConversations.Add(conversation);
        await ctx.SaveChangesAsync();

        await using var ctx2 = _fixture.CreateDbContext();
        var loaded = await ctx2.ChatConversations.FirstAsync(c => c.Id == conversation.Id);
        Assert.Equal(specialContent, loaded.Messages[0].Content);
    }

    [Fact]
    public async Task SubstackDetection_CanBeCreatedAndPersisted()
    {
        await using var ctx = await CreateContextAsync();

        var detection = new SubstackDetection
        {
            RssGuid = $"guid-{Guid.NewGuid():N}",
            Title = "Test Post",
            SubstackUrl = $"https://test.substack.com/p/test-{Guid.NewGuid():N}",
            PublishedAt = DateTimeOffset.UtcNow,
            DetectedAt = DateTimeOffset.UtcNow,
            Confidence = MatchConfidence.High,
            ContentHash = "abc123def456abc123def456abc123def456abc123def456abc123def456abcd",
        };
        ctx.SubstackDetections.Add(detection);
        await ctx.SaveChangesAsync();

        await using var ctx2 = _fixture.CreateDbContext();
        var loaded = await ctx2.SubstackDetections.FirstAsync(s => s.Id == detection.Id);
        Assert.Equal(detection.RssGuid, loaded.RssGuid);
        Assert.Equal(MatchConfidence.High, loaded.Confidence);
    }

    [Fact]
    public async Task SubstackDetection_RssGuid_UniqueIndexPreventsDuplicates()
    {
        await using var ctx = await CreateContextAsync();
        var guid = $"unique-guid-{Guid.NewGuid():N}";

        ctx.SubstackDetections.Add(new SubstackDetection
        {
            RssGuid = guid,
            Title = "First",
            SubstackUrl = $"https://test.substack.com/p/first-{Guid.NewGuid():N}",
            PublishedAt = DateTimeOffset.UtcNow,
            DetectedAt = DateTimeOffset.UtcNow,
            Confidence = MatchConfidence.High,
            ContentHash = "abc123def456abc123def456abc123def456abc123def456abc123def456abcd",
        });
        await ctx.SaveChangesAsync();

        ctx.SubstackDetections.Add(new SubstackDetection
        {
            RssGuid = guid,
            Title = "Duplicate",
            SubstackUrl = $"https://test.substack.com/p/dup-{Guid.NewGuid():N}",
            PublishedAt = DateTimeOffset.UtcNow,
            DetectedAt = DateTimeOffset.UtcNow,
            Confidence = MatchConfidence.Low,
            ContentHash = "xyz789xyz789xyz789xyz789xyz789xyz789xyz789xyz789xyz789xyz789xyzx",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task SubstackDetection_SubstackUrl_UniqueIndexPreventsDuplicates()
    {
        await using var ctx = await CreateContextAsync();
        var url = $"https://test.substack.com/p/unique-{Guid.NewGuid():N}";

        ctx.SubstackDetections.Add(new SubstackDetection
        {
            RssGuid = $"guid-a-{Guid.NewGuid():N}",
            Title = "First",
            SubstackUrl = url,
            PublishedAt = DateTimeOffset.UtcNow,
            DetectedAt = DateTimeOffset.UtcNow,
            Confidence = MatchConfidence.High,
            ContentHash = "abc123def456abc123def456abc123def456abc123def456abc123def456abcd",
        });
        await ctx.SaveChangesAsync();

        ctx.SubstackDetections.Add(new SubstackDetection
        {
            RssGuid = $"guid-b-{Guid.NewGuid():N}",
            Title = "Duplicate URL",
            SubstackUrl = url,
            PublishedAt = DateTimeOffset.UtcNow,
            DetectedAt = DateTimeOffset.UtcNow,
            Confidence = MatchConfidence.Low,
            ContentHash = "xyz789xyz789xyz789xyz789xyz789xyz789xyz789xyz789xyz789xyz789xyzx",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task UserNotification_CanBeCreatedAndPersisted_WithPendingStatus()
    {
        await using var ctx = await CreateContextAsync();

        var notification = new UserNotification
        {
            Type = "SubstackDetected",
            Message = "New post detected: Test Title",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.UserNotifications.Add(notification);
        await ctx.SaveChangesAsync();

        await using var ctx2 = _fixture.CreateDbContext();
        var loaded = await ctx2.UserNotifications.FirstAsync(n => n.Id == notification.Id);
        Assert.Equal(NotificationStatus.Pending, loaded.Status);
        Assert.Equal("SubstackDetected", loaded.Type);
    }

    [Fact]
    public async Task UserNotification_UniqueFilteredIndex_OnContentIdAndType_WherePending()
    {
        await using var ctx = await CreateContextAsync();
        var content = CreateContent();
        ctx.Contents.Add(content);

        ctx.UserNotifications.Add(new UserNotification
        {
            Type = "SubstackDetected",
            Message = "First notification",
            ContentId = content.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();

        ctx.UserNotifications.Add(new UserNotification
        {
            Type = "SubstackDetected",
            Message = "Duplicate pending",
            ContentId = content.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task BlogPublishRequest_CanBeCreatedAndPersisted()
    {
        await using var ctx = await CreateContextAsync();
        var content = CreateContent();
        ctx.Contents.Add(content);

        var request = new BlogPublishRequest
        {
            ContentId = content.Id,
            Html = "<h1>Blog Post</h1><p>Content here</p>",
            TargetPath = "content/blog/test-post.html",
        };
        ctx.BlogPublishRequests.Add(request);
        await ctx.SaveChangesAsync();

        await using var ctx2 = _fixture.CreateDbContext();
        var loaded = await ctx2.BlogPublishRequests.FirstAsync(b => b.Id == request.Id);
        Assert.Equal(BlogPublishStatus.Staged, loaded.Status);
        Assert.Equal(content.Id, loaded.ContentId);
        Assert.Equal(0, loaded.VerificationAttempts);
    }

    [Fact]
    public async Task Content_NewColumns_PersistCorrectly()
    {
        await using var ctx = await CreateContextAsync();
        var content = CreateContent();
        content.SubstackPostUrl = "https://test.substack.com/p/my-post";
        content.BlogPostUrl = "https://matthewkruczek.ai/blog/my-post";
        content.BlogDeployCommitSha = "abc123def456abc123def456abc123def456abcd";
        content.BlogDelayOverride = TimeSpan.FromDays(3);
        content.BlogSkipped = false;
        ctx.Contents.Add(content);
        await ctx.SaveChangesAsync();

        await using var ctx2 = _fixture.CreateDbContext();
        var loaded = await ctx2.Contents.FirstAsync(c => c.Id == content.Id);
        Assert.Equal("https://test.substack.com/p/my-post", loaded.SubstackPostUrl);
        Assert.Equal("https://matthewkruczek.ai/blog/my-post", loaded.BlogPostUrl);
        Assert.Equal("abc123def456abc123def456abc123def456abcd", loaded.BlogDeployCommitSha);
        Assert.Equal(TimeSpan.FromDays(3), loaded.BlogDelayOverride);
        Assert.False(loaded.BlogSkipped);
    }

    [Fact]
    public async Task Content_BlogDelayOverride_StoresTimeSpanCorrectly()
    {
        await using var ctx = await CreateContextAsync();

        var contentWithDelay = CreateContent();
        contentWithDelay.BlogDelayOverride = TimeSpan.FromHours(48);
        ctx.Contents.Add(contentWithDelay);

        var contentWithoutDelay = CreateContent();
        ctx.Contents.Add(contentWithoutDelay);
        await ctx.SaveChangesAsync();

        await using var ctx2 = _fixture.CreateDbContext();
        var withDelay = await ctx2.Contents.FirstAsync(c => c.Id == contentWithDelay.Id);
        var withoutDelay = await ctx2.Contents.FirstAsync(c => c.Id == contentWithoutDelay.Id);

        Assert.Equal(TimeSpan.FromHours(48), withDelay.BlogDelayOverride);
        Assert.Null(withoutDelay.BlogDelayOverride);
    }

    [Fact]
    public async Task Content_BlogSkipped_DefaultsToFalse()
    {
        await using var ctx = await CreateContextAsync();
        var content = CreateContent();
        ctx.Contents.Add(content);
        await ctx.SaveChangesAsync();

        await using var ctx2 = _fixture.CreateDbContext();
        var loaded = await ctx2.Contents.FirstAsync(c => c.Id == content.Id);
        Assert.False(loaded.BlogSkipped);
    }

    [Fact]
    public async Task ContentPlatformStatus_ScheduledAt_PersistsCorrectly()
    {
        await using var ctx = await CreateContextAsync();
        var content = CreateContent();
        ctx.Contents.Add(content);

        var scheduledTime = DateTimeOffset.UtcNow.AddDays(7);
        var status = new ContentPlatformStatus
        {
            ContentId = content.Id,
            Platform = PlatformType.PersonalBlog,
            ScheduledAt = scheduledTime,
        };
        ctx.ContentPlatformStatuses.Add(status);
        await ctx.SaveChangesAsync();

        await using var ctx2 = _fixture.CreateDbContext();
        var loaded = await ctx2.ContentPlatformStatuses.FirstAsync(s => s.Id == status.Id);
        Assert.NotNull(loaded.ScheduledAt);
        Assert.Equal(scheduledTime.UtcDateTime, loaded.ScheduledAt!.Value.UtcDateTime, TimeSpan.FromSeconds(1));
    }
}
