using Microsoft.EntityFrameworkCore;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Commands;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Commands;

public class DraftContentHandlerTests
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
    public async Task Handle_CallsSidecarWithDraftPrompt()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "My Post",
            Body = "Some context",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog,
            Status = ContentStatus.Idea
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        string? capturedPrompt = null;
        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, CancellationToken>((_, userPrompt, _, _) => capturedPrompt = userPrompt)
            .ReturnsAsync("Generated content");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        await handler.Handle(
            new DraftContent.Command(content.Id, "draft", null, null), CancellationToken.None);

        Assert.NotNull(capturedPrompt);
        Assert.Contains("Generate a", capturedPrompt);
        Assert.Contains("My Post", capturedPrompt);
    }

    [Fact]
    public async Task Handle_CallsSidecarWithRefinePrompt()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Post",
            Body = "Rough draft",
            Status = ContentStatus.Draft,
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        string? capturedPrompt = null;
        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, CancellationToken>((_, userPrompt, _, _) => capturedPrompt = userPrompt)
            .ReturnsAsync("Refined content");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        await handler.Handle(
            new DraftContent.Command(content.Id, "refine", null, null), CancellationToken.None);

        Assert.Contains("Improve this", capturedPrompt!);
    }

    [Fact]
    public async Task Handle_CallsSidecarWithShortenPrompt()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Post",
            Body = "Long text",
            Status = ContentStatus.Draft,
            ContentType = ContentType.Tweet,
            PrimaryPlatform = Platform.Twitter
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        string? capturedPrompt = null;
        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, CancellationToken>((_, userPrompt, _, _) => capturedPrompt = userPrompt)
            .ReturnsAsync("Short");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        await handler.Handle(
            new DraftContent.Command(content.Id, "shorten", null, null), CancellationToken.None);

        Assert.Contains("Shorten this", capturedPrompt!);
    }

    [Fact]
    public async Task Handle_CallsSidecarWithExpandPrompt()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Post",
            Body = "Brief",
            Status = ContentStatus.Draft,
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        string? capturedPrompt = null;
        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, CancellationToken>((_, userPrompt, _, _) => capturedPrompt = userPrompt)
            .ReturnsAsync("Expanded content");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        await handler.Handle(
            new DraftContent.Command(content.Id, "expand", null, null), CancellationToken.None);

        Assert.Contains("Expand this", capturedPrompt!);
    }

    [Fact]
    public async Task Handle_CallsSidecarWithChangeTonePrompt()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Post",
            Body = "Casual text",
            Status = ContentStatus.Draft,
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        string? capturedPrompt = null;
        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, CancellationToken>((_, userPrompt, _, _) => capturedPrompt = userPrompt)
            .ReturnsAsync("Professional text");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        await handler.Handle(
            new DraftContent.Command(content.Id, "changeTone", null, "professional"), CancellationToken.None);

        Assert.Contains("professional", capturedPrompt!);
    }

    [Fact]
    public async Task Handle_SystemPromptIncludesBrandProfile()
    {
        await using var context = CreateContext();
        var profile = new BrandProfile
        {
            Personality = "Witty",
            Tone = "Conversational",
            Vocabulary = ["innovative", "cutting-edge"],
            AvoidWords = ["synergy"]
        };
        context.BrandProfiles.Add(profile);
        var content = new ContentEntity
        {
            Title = "Post",
            Body = "Text",
            Status = ContentStatus.Draft,
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        string? capturedSystemPrompt = null;
        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, CancellationToken>((systemPrompt, _, _, _) => capturedSystemPrompt = systemPrompt)
            .ReturnsAsync("Content");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        await handler.Handle(
            new DraftContent.Command(content.Id, "draft", null, null), CancellationToken.None);

        Assert.Contains("Witty", capturedSystemPrompt!);
        Assert.Contains("Conversational", capturedSystemPrompt);
        Assert.Contains("innovative", capturedSystemPrompt);
    }

    [Fact]
    public async Task Handle_TransitionsFromIdeaToDraft()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Post",
            Body = "",
            Status = ContentStatus.Idea,
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Generated");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        await handler.Handle(
            new DraftContent.Command(content.Id, "draft", null, null), CancellationToken.None);

        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Draft, reloaded!.Status);
    }

    [Fact]
    public async Task Handle_DoesNotChangeStatusWhenAlreadyDraft()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Post",
            Body = "Existing",
            Status = ContentStatus.Draft,
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Refined");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        await handler.Handle(
            new DraftContent.Command(content.Id, "refine", null, null), CancellationToken.None);

        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Draft, reloaded!.Status);
    }

    [Fact]
    public async Task Handle_UpdatesBodyWithSidecarResponse()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Post",
            Body = "Old",
            Status = ContentStatus.Draft,
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Brand new content from sidecar");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        await handler.Handle(
            new DraftContent.Command(content.Id, "draft", null, null), CancellationToken.None);

        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal("Brand new content from sidecar", reloaded!.Body);
    }

    [Fact]
    public async Task Handle_ReturnsUpdatedContentDetailDto()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Post",
            Body = "Old",
            Status = ContentStatus.Draft,
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("New body");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(
            new DraftContent.Command(content.Id, "draft", null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New body", result.Value!.Body);
        Assert.Equal(content.Id, result.Value.Id);
    }

    [Fact]
    public async Task Handle_HandlesMissingBrandProfile()
    {
        await using var context = CreateContext();
        var content = new ContentEntity
        {
            Title = "Post",
            Body = "Text",
            Status = ContentStatus.Draft,
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Content");

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(
            new DraftContent.Command(content.Id, "draft", null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenContentDoesNotExist()
    {
        await using var context = CreateContext();

        var handler = new DraftContent.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(
            new DraftContent.Command(Guid.NewGuid(), "draft", null, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
