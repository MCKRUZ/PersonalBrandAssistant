using Microsoft.EntityFrameworkCore;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Commands;

public class GenerateCrossPostHandlerTests
{
    private readonly Mock<ISidecarClient> _sidecarMock = new();

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_CreatesChildContentWithParentContentIdSet()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity
        {
            Title = "Original Post",
            Body = "Original body content",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog,
            Status = ContentStatus.Draft
        };
        context.Contents.Add(parent);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Adapted for Twitter");

        var handler = new GenerateCrossPost.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(
            new GenerateCrossPost.Command(parent.Id, Platform.Twitter), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var child = await context.Contents.FindAsync(result.Value);
        Assert.Equal(parent.Id, child!.ParentContentId);
    }

    [Fact]
    public async Task Handle_ChildHasTargetPlatformAndDraftStatus()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity
        {
            Title = "Post",
            Body = "Content",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog,
            Status = ContentStatus.Draft
        };
        context.Contents.Add(parent);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("LinkedIn version");

        var handler = new GenerateCrossPost.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(
            new GenerateCrossPost.Command(parent.Id, Platform.LinkedIn), CancellationToken.None);

        var child = await context.Contents.FindAsync(result.Value);
        Assert.Equal(Platform.LinkedIn, child!.PrimaryPlatform);
        Assert.Equal(ContentStatus.Draft, child.Status);
    }

    [Fact]
    public async Task Handle_SidecarPromptIncludesPlatformConstraints()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity
        {
            Title = "Post",
            Body = "Content",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog,
            Status = ContentStatus.Draft
        };
        context.Contents.Add(parent);
        await context.SaveChangesAsync();

        string? capturedPrompt = null;
        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, userPrompt, _) => capturedPrompt = userPrompt)
            .ReturnsAsync("Adapted");

        var handler = new GenerateCrossPost.Handler(context, _sidecarMock.Object);
        await handler.Handle(
            new GenerateCrossPost.Command(parent.Id, Platform.Twitter), CancellationToken.None);

        Assert.Contains("Twitter", capturedPrompt!);
    }

    [Fact]
    public async Task Handle_ReturnsChildContentId()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity
        {
            Title = "Post",
            Body = "Content",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog,
            Status = ContentStatus.Draft
        };
        context.Contents.Add(parent);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Cross-posted");

        var handler = new GenerateCrossPost.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(
            new GenerateCrossPost.Command(parent.Id, Platform.LinkedIn), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        Assert.NotEqual(parent.Id, result.Value);
    }

    [Fact]
    public async Task Handle_ChildBodyIsSidecarResponse()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity
        {
            Title = "Post",
            Body = "Original",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog,
            Status = ContentStatus.Draft
        };
        context.Contents.Add(parent);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Sidecar generated this");

        var handler = new GenerateCrossPost.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(
            new GenerateCrossPost.Command(parent.Id, Platform.Twitter), CancellationToken.None);

        var child = await context.Contents.FindAsync(result.Value);
        Assert.Equal("Sidecar generated this", child!.Body);
    }

    [Fact]
    public async Task Handle_RejectsSamePlatformCrossPost()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity
        {
            Title = "Post",
            Body = "Content",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog,
            Status = ContentStatus.Draft
        };
        context.Contents.Add(parent);
        await context.SaveChangesAsync();

        var handler = new GenerateCrossPost.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(
            new GenerateCrossPost.Command(parent.Id, Platform.Blog), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("differ", result.Errors[0]);
    }

    [Fact]
    public async Task Handle_RejectsDuplicateCrossPost()
    {
        await using var context = CreateContext();
        var parent = new ContentEntity
        {
            Title = "Post",
            Body = "Content",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog,
            Status = ContentStatus.Draft
        };
        var existingChild = new ContentEntity
        {
            Title = "Existing",
            Body = "Already adapted",
            ContentType = ContentType.Tweet,
            PrimaryPlatform = Platform.Twitter,
            Status = ContentStatus.Draft,
            ParentContentId = parent.Id
        };
        context.Contents.AddRange(parent, existingChild);
        await context.SaveChangesAsync();

        var handler = new GenerateCrossPost.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(
            new GenerateCrossPost.Command(parent.Id, Platform.Twitter), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.Errors[0]);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenParentDoesNotExist()
    {
        await using var context = CreateContext();

        var handler = new GenerateCrossPost.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(
            new GenerateCrossPost.Command(Guid.NewGuid(), Platform.Twitter), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
