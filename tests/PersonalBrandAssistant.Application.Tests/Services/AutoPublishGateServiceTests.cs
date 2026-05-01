using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;

namespace PersonalBrandAssistant.Application.Tests.Services;

public class AutoPublishGateServiceTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<IBrandVoiceService> _brandVoiceService = new();
    private readonly Mock<ILogger<AutoPublishGateService>> _logger = new();

    private AutoPublishGateService CreateSut() => new(
        _dbContext.Object,
        _brandVoiceService.Object,
        _logger.Object);

    private AutonomyConfiguration CreateConfig(int threshold = 90) =>
        AutonomyConfiguration.CreateDefault();

    private void SetupDbSets(Content[]? contents = null, AutonomyConfiguration? config = null)
    {
        var contentMock = (contents ?? []).AsQueryable().BuildMockDbSet();
        contentMock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns<object[], CancellationToken>((keys, _) =>
                ValueTask.FromResult((contents ?? []).FirstOrDefault(c => c.Id == (Guid)keys[0])));
        _dbContext.Setup(d => d.Contents).Returns(contentMock.Object);

        var configs = config is not null ? new[] { config } : Array.Empty<AutonomyConfiguration>();
        var configMock = configs.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.AutonomyConfigurations).Returns(configMock.Object);
    }

    [Fact]
    public async Task EvaluateAsync_SkipsGatesForDraftLevel()
    {
        var sut = CreateSut();
        var result = await sut.EvaluateAsync(Guid.NewGuid(), AutonomyLevel.Draft);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Approved);
    }

    [Fact]
    public async Task EvaluateAsync_SkipsGatesForManualLevel()
    {
        var sut = CreateSut();
        var result = await sut.EvaluateAsync(Guid.NewGuid(), AutonomyLevel.Manual);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Approved);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsContentWithRuleViolations()
    {
        var content = Content.Create(ContentType.SocialPost, "Content with synergy");
        var config = CreateConfig();
        SetupDbSets(contents: [content], config: config);

        _brandVoiceService.Setup(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BrandVoiceScore>.Success(
                BrandVoiceScore.Create(95, 95, 95, 95, [], ["Avoided term: synergy"])));

        var sut = CreateSut();
        var result = await sut.EvaluateAsync(content.Id, AutonomyLevel.AutoPublish);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Approved);
        Assert.Contains(result.Value.FailureReasons, r => r.Contains("rule violation"));
    }

    [Fact]
    public async Task EvaluateAsync_RejectsContentExceedingPlatformCharLimit()
    {
        var longBody = new string('x', 300);
        var content = Content.Create(ContentType.SocialPost, longBody, targetPlatforms: [PlatformType.TwitterX]);
        var config = CreateConfig();
        SetupDbSets(contents: [content], config: config);

        _brandVoiceService.Setup(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BrandVoiceScore>.Success(
                BrandVoiceScore.Create(95, 95, 95, 95, [], [])));

        var sut = CreateSut();
        var result = await sut.EvaluateAsync(content.Id, AutonomyLevel.AutoPublish);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Approved);
        Assert.Contains(result.Value.FailureReasons, r => r.Contains("character limit"));
    }

    [Fact]
    public async Task EvaluateAsync_RejectsContentOutsidePostingHours()
    {
        var content = Content.Create(ContentType.SocialPost, "Short post");
        content.ScheduledAt = new DateTimeOffset(2026, 5, 1, 3, 0, 0, TimeSpan.Zero);
        var config = CreateConfig();
        SetupDbSets(contents: [content], config: config);

        _brandVoiceService.Setup(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BrandVoiceScore>.Success(
                BrandVoiceScore.Create(95, 95, 95, 95, [], [])));

        var sut = CreateSut();
        var result = await sut.EvaluateAsync(content.Id, AutonomyLevel.AutoPublish);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Approved);
        Assert.Contains(result.Value.FailureReasons, r => r.Contains("posting hours"));
    }

    [Fact]
    public async Task EvaluateAsync_RejectsNonAllowlistedDomains()
    {
        var content = Content.Create(ContentType.SocialPost, "Check out https://sketchy-site.xyz/page");
        var config = CreateConfig();
        SetupDbSets(contents: [content], config: config);

        _brandVoiceService.Setup(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BrandVoiceScore>.Success(
                BrandVoiceScore.Create(95, 95, 95, 95, [], [])));

        var sut = CreateSut();
        var result = await sut.EvaluateAsync(content.Id, AutonomyLevel.AutoPublish);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Approved);
        Assert.Contains(result.Value.FailureReasons, r => r.Contains("non-allowlisted domain"));
    }

    [Fact]
    public async Task EvaluateAsync_ApprovesWhenAllGatesPass()
    {
        var content = Content.Create(ContentType.SocialPost, "Great post about AI https://matthewkruczek.ai");
        content.ScheduledAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var config = CreateConfig();
        SetupDbSets(contents: [content], config: config);

        _brandVoiceService.Setup(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BrandVoiceScore>.Success(
                BrandVoiceScore.Create(95, 95, 95, 95, [], [])));

        var sut = CreateSut();
        var result = await sut.EvaluateAsync(content.Id, AutonomyLevel.AutoPublish);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Approved);
        Assert.Empty(result.Value.FailureReasons);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsWhenScoreBelowThreshold()
    {
        var content = Content.Create(ContentType.SocialPost, "Low quality post");
        content.ScheduledAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var config = CreateConfig();
        SetupDbSets(contents: [content], config: config);

        _brandVoiceService.Setup(s => s.ScoreContentAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BrandVoiceScore>.Success(
                BrandVoiceScore.Create(50, 50, 50, 50, [], [])));

        var sut = CreateSut();
        var result = await sut.EvaluateAsync(content.Id, AutonomyLevel.AutoPublish);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Approved);
        Assert.Contains(result.Value.FailureReasons, r => r.Contains("below threshold"));
    }
}
