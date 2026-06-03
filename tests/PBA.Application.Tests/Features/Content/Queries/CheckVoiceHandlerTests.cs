using Microsoft.EntityFrameworkCore;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Content.Queries;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Tests.Features.Content.Queries;

public class CheckVoiceHandlerTests
{
    private readonly Mock<ISidecarClient> _sidecarMock = new();

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_ValidContent_ReturnsScoreAndFeedback()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Some body text" };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 85, \"feedback\": \"Good match\"}");

        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(85m, result.Value!.Score);
        Assert.Equal("Good match", result.Value.Feedback);
    }

    [Fact]
    public async Task Handle_ValidContent_UpdatesVoiceScoreOnEntity()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Some body text" };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 85, \"feedback\": \"Good match\"}");

        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
        await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);

        var reloaded = await context.Contents.FindAsync(content.Id);
        Assert.Equal(85m, reloaded!.VoiceScore);
    }

    [Fact]
    public async Task Handle_NonExistentContent_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(new CheckVoice.Query(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }

    [Fact]
    public async Task Handle_MissingBrandProfile_UsesDefaults()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Some body" };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 50, \"feedback\": \"Default profile\"}");

        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_SidecarPrompt_ContainsStructuredJsonInstruction()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Some body" };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        string? capturedUserPrompt = null;
        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, userPrompt, _) => capturedUserPrompt = userPrompt)
            .ReturnsAsync("{\"score\": 75, \"feedback\": \"OK\"}");

        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
        await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);

        Assert.NotNull(capturedUserPrompt);
        Assert.Contains("Respond ONLY with JSON", capturedUserPrompt);
    }

    [Fact]
    public async Task Handle_SidecarReturnsInvalidJson_ReturnsFailure()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Some body" };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("This is not valid JSON at all");

        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_SidecarWrapsJsonInMarkdownFence_ReturnsScore()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Some body" };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("```json\n{\"score\": 82, \"feedback\": \"Strong match\"}\n```");

        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(82m, result.Value!.Score);
        Assert.Equal("Strong match", result.Value.Feedback);
    }

    [Fact]
    public async Task Handle_SidecarAddsProseAroundJson_ReturnsScore()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Some body" };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Here is my analysis:\n{\"score\": 64, \"feedback\": \"Decent\"}\nLet me know if you need more.");

        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(64m, result.Value!.Score);
        Assert.Equal("Decent", result.Value.Feedback);
    }

    [Fact]
    public async Task Handle_ScoreOutOfRange_ReturnsFailure()
    {
        await using var context = CreateContext();
        var content = new ContentEntity { Title = "Test", Body = "Some body" };
        context.Contents.Add(content);
        await context.SaveChangesAsync();

        _sidecarMock
            .Setup(s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 150, \"feedback\": \"Manipulated\"}");

        var handler = new CheckVoice.Handler(context, _sidecarMock.Object);
        var result = await handler.Handle(new CheckVoice.Query(content.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
