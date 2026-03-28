using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Services.BlogServices;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.BlogServices;

[Collection("Postgres")]
public class BlogSchedulingServiceTests
{
    private readonly PostgresFixture _fixture;

    public BlogSchedulingServiceTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<(BlogSchedulingService sut, ApplicationDbContext db, Mock<INotificationService> notifications)>
        CreateSutAsync(PublishDelayOptions? options = null)
    {
        var db = _fixture.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var notifications = new Mock<INotificationService>();
        var opts = Options.Create(options ?? new PublishDelayOptions
        {
            DefaultSubstackToBlogDelay = TimeSpan.FromDays(7),
            RequiresConfirmation = true
        });
        var sut = new BlogSchedulingService(db, notifications.Object, opts,
            NullLogger<BlogSchedulingService>.Instance);
        return (sut, db, notifications);
    }

    [Fact]
    public async Task SubstackPublication_TriggersNotification_WhenRequiresConfirmation()
    {
        var (sut, db, notifications) = await CreateSutAsync();
        var content = Content.Create(ContentType.BlogPost, "body", "Notify Test",
            [PlatformType.Substack, PlatformType.PersonalBlog]);
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        var substackPublishedAt = DateTimeOffset.UtcNow;
        await sut.OnSubstackPublicationConfirmedAsync(content.Id, substackPublishedAt, default);

        notifications.Verify(n => n.SendAsync(
            NotificationType.BlogReady,
            It.IsAny<string>(),
            It.IsAny<string>(),
            content.Id,
            It.IsAny<CancellationToken>()), Times.Once);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ConfirmBlogSchedule_CreatesScheduledPlatformStatus()
    {
        var (sut, db, _) = await CreateSutAsync();
        var content = Content.Create(ContentType.BlogPost, "body", "Confirm Test",
            [PlatformType.Substack, PlatformType.PersonalBlog]);
        db.Contents.Add(content);

        var substackStatus = new ContentPlatformStatus
        {
            ContentId = content.Id,
            Platform = PlatformType.Substack,
            Status = PlatformPublishStatus.Published,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        db.ContentPlatformStatuses.Add(substackStatus);
        await db.SaveChangesAsync();

        var result = await sut.ConfirmBlogScheduleAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        var blogStatus = await db.ContentPlatformStatuses
            .FirstOrDefaultAsync(s => s.ContentId == content.Id && s.Platform == PlatformType.PersonalBlog);
        Assert.NotNull(blogStatus);
        Assert.Equal(PlatformPublishStatus.Pending, blogStatus.Status);
        Assert.NotNull(blogStatus.ScheduledAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ValidateBlogPublishAllowed_Blocked_WhenSubstackNotPublished()
    {
        var (sut, db, _) = await CreateSutAsync();
        var content = Content.Create(ContentType.BlogPost, "body", "Block Test",
            [PlatformType.Substack, PlatformType.PersonalBlog]);
        db.Contents.Add(content);

        var substackStatus = new ContentPlatformStatus
        {
            ContentId = content.Id,
            Platform = PlatformType.Substack,
            Status = PlatformPublishStatus.Pending
        };
        db.ContentPlatformStatuses.Add(substackStatus);
        await db.SaveChangesAsync();

        var result = await sut.ValidateBlogPublishAllowedAsync(content.Id, default);

        Assert.False(result.IsSuccess);
        Assert.Contains("Substack must be published", result.Errors.First());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ValidateBlogPublishAllowed_Allowed_WhenSubstackPublished()
    {
        var (sut, db, _) = await CreateSutAsync();
        var content = Content.Create(ContentType.BlogPost, "body", "Allow Test",
            [PlatformType.Substack, PlatformType.PersonalBlog]);
        db.Contents.Add(content);

        var substackStatus = new ContentPlatformStatus
        {
            ContentId = content.Id,
            Platform = PlatformType.Substack,
            Status = PlatformPublishStatus.Published,
            PublishedAt = DateTimeOffset.UtcNow
        };
        db.ContentPlatformStatuses.Add(substackStatus);
        await db.SaveChangesAsync();

        var result = await sut.ValidateBlogPublishAllowedAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task BlogSkipped_NoNotificationSent()
    {
        var (sut, db, notifications) = await CreateSutAsync();
        var content = Content.Create(ContentType.BlogPost, "body", "Skip Test",
            [PlatformType.Substack, PlatformType.PersonalBlog]);
        content.BlogSkipped = true;
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        await sut.OnSubstackPublicationConfirmedAsync(content.Id, DateTimeOffset.UtcNow, default);

        notifications.Verify(n => n.SendAsync(
            It.IsAny<NotificationType>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        await db.DisposeAsync();
    }
}
