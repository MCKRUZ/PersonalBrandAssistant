using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
using System.Text.Json;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Application.Tests.Services.BrandVoice;

public class BrandVoiceServiceTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<ISidecarClient> _sidecar = new();
    private readonly Mock<IContentPipeline> _pipeline = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<ILogger<BrandVoiceService>> _logger = new();
    private readonly ContentEngineOptions _options = new();

    public BrandVoiceServiceTests()
    {
        _serviceProvider.Setup(sp => sp.GetService(typeof(IContentPipeline)))
            .Returns(_pipeline.Object);
    }

    private BrandVoiceService CreateSut() => new(
        _dbContext.Object,
        _sidecar.Object,
        _serviceProvider.Object,
        Options.Create(_options),
        _logger.Object);

    private BrandProfile CreateProfile(
        List<string>? avoidTerms = null,
        List<string>? preferredTerms = null) => new()
    {
        Name = "Test Profile",
        ToneDescriptors = ["professional", "friendly"],
        StyleGuidelines = "Be concise.",
        VocabularyPreferences = new VocabularyConfig
        {
            AvoidTerms = avoidTerms ?? [],
            PreferredTerms = preferredTerms ?? [],
        },
        Topics = ["tech"],
        PersonaDescription = "A tech leader",
        ExampleContent = [],
        IsActive = true,
    };

    private void SetupDbSets(Content[]? contents = null, BrandProfile[]? profiles = null)
    {
        var contentMock = (contents ?? []).AsQueryable().BuildMockDbSet();
        contentMock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns<object[], CancellationToken>((keys, _) =>
                ValueTask.FromResult((contents ?? []).FirstOrDefault(c => c.Id == (Guid)keys[0])));
        _dbContext.Setup(d => d.Contents).Returns(contentMock.Object);

        var profileMock = (profiles ?? []).AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.BrandProfiles).Returns(profileMock.Object);
    }

    private void SetupSidecarResponse(string jsonResponse)
    {
        _sidecar.Setup(s => s.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(
                new ChatEvent("text", jsonResponse, null, null),
                new TaskCompleteEvent("session-1", 100, 50)));
    }

    private static async IAsyncEnumerable<SidecarEvent> CreateAsyncEnumerable(
        params SidecarEvent[] events)
    {
        foreach (var e in events)
        {
            yield return e;
            await Task.CompletedTask;
        }
    }

    // --- RunRuleChecks ---

    [Fact]
    public void RunRuleChecks_DetectsAvoidedTerms()
    {
        var profile = CreateProfile(avoidTerms: ["leverage", "synergy"]);
        var sut = CreateSut();

        var result = sut.RunRuleChecks("We leverage synergy to maximize impact", profile);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value, v => v.Contains("leverage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Value, v => v.Contains("synergy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunRuleChecks_WarnsWhenNoPreferredTermsPresent()
    {
        var profile = CreateProfile(preferredTerms: ["AI", "automation", "branding"]);
        var sut = CreateSut();

        var result = sut.RunRuleChecks("A generic post about nothing specific", profile);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Contains("preferred", result.Value![0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunRuleChecks_ReturnsEmptyForCompliantContent()
    {
        var profile = CreateProfile(
            preferredTerms: ["AI"],
            avoidTerms: ["leverage"]);
        var sut = CreateSut();

        var result = sut.RunRuleChecks("AI is transforming the industry", profile);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public void RunRuleChecks_IsCaseInsensitive()
    {
        var profile = CreateProfile(avoidTerms: ["leverage"]);
        var sut = CreateSut();

        var result = sut.RunRuleChecks("We LEVERAGE this", profile);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    [Fact]
    public void RunRuleChecks_StripsHtmlBeforeChecking()
    {
        var profile = CreateProfile(avoidTerms: ["bold"]);
        var sut = CreateSut();

        var result = sut.RunRuleChecks("<p>This is <b>bold</b> text</p>", profile);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    // --- ScoreContentAsync ---

    [Fact]
    public async Task ScoreContentAsync_ParsesJsonResponseIntoBrandVoiceScore()
    {
        var content = Content.Create(ContentType.SocialPost, "Great AI content");
        var profile = CreateProfile();
        SetupDbSets(contents: [content], profiles: [profile]);

        var json = JsonSerializer.Serialize(new
        {
            overallScore = 85,
            toneAlignment = 90,
            vocabularyConsistency = 80,
            personaFidelity = 85,
            issues = new[] { "Minor tone shift" },
        });
        SetupSidecarResponse(json);

        var sut = CreateSut();
        var result = await sut.ScoreContentAsync(content.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(85, result.Value!.OverallScore);
        Assert.Equal(90, result.Value.ToneAlignment);
        Assert.Equal(80, result.Value.VocabularyConsistency);
        Assert.Equal(85, result.Value.PersonaFidelity);
    }

    [Fact]
    public async Task ScoreContentAsync_HandlesInvalidJsonGracefully()
    {
        var content = Content.Create(ContentType.SocialPost, "Some content");
        var profile = CreateProfile();
        SetupDbSets(contents: [content], profiles: [profile]);
        SetupSidecarResponse("I think the score is about 7 out of 10");

        var sut = CreateSut();
        var result = await sut.ScoreContentAsync(content.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ScoreContentAsync_ReturnsNotFoundWhenContentMissing()
    {
        SetupDbSets();

        var sut = CreateSut();
        var result = await sut.ScoreContentAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task ScoreContentAsync_StoresScoreInContentMetadata()
    {
        var content = Content.Create(ContentType.SocialPost, "Great AI content");
        var profile = CreateProfile();
        SetupDbSets(contents: [content], profiles: [profile]);

        var json = JsonSerializer.Serialize(new
        {
            overallScore = 85,
            toneAlignment = 90,
            vocabularyConsistency = 80,
            personaFidelity = 85,
            issues = Array.Empty<string>(),
        });
        SetupSidecarResponse(json);

        var sut = CreateSut();
        await sut.ScoreContentAsync(content.Id, CancellationToken.None);

        Assert.True(content.Metadata.PlatformSpecificData.ContainsKey("BrandVoiceScore"));
    }

    [Fact]
    public async Task ScoreContentAsync_SendsPromptToSidecar()
    {
        var content = Content.Create(ContentType.SocialPost, "AI content");
        var profile = CreateProfile();
        SetupDbSets(contents: [content], profiles: [profile]);

        var json = JsonSerializer.Serialize(new
        {
            overallScore = 85, toneAlignment = 90,
            vocabularyConsistency = 80, personaFidelity = 85,
            issues = Array.Empty<string>(),
        });
        SetupSidecarResponse(json);

        var sut = CreateSut();
        await sut.ScoreContentAsync(content.Id, CancellationToken.None);

        _sidecar.Verify(s => s.SendTaskAsync(
            It.Is<string>(p => p.Contains("AI content") && p.Contains("professional")),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- ValidateAndGateAsync ---

    [Fact]
    public async Task ValidateAndGateAsync_AutonomousAutoRegeneratesBelowThreshold()
    {
        var content = Content.Create(ContentType.SocialPost, "Bad content");
        var profile = CreateProfile();
        SetupDbSets(contents: [content], profiles: [profile]);

        var lowJson = JsonSerializer.Serialize(new
        {
            overallScore = 50, toneAlignment = 50,
            vocabularyConsistency = 50, personaFidelity = 50,
            issues = Array.Empty<string>(),
        });
        var highJson = JsonSerializer.Serialize(new
        {
            overallScore = 80, toneAlignment = 80,
            vocabularyConsistency = 80, personaFidelity = 80,
            issues = Array.Empty<string>(),
        });

        var callCount = 0;
        _sidecar.Setup(s => s.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                var json = callCount <= 1 ? lowJson : highJson;
                return CreateAsyncEnumerable(
                    new ChatEvent("text", json, null, null),
                    new TaskCompleteEvent("s1", 100, 50));
            });

        _pipeline.Setup(p => p.GenerateDraftAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("Regenerated"));

        var sut = CreateSut();
        var result = await sut.ValidateAndGateAsync(content.Id, AutonomyLevel.Autonomous, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _pipeline.Verify(p => p.GenerateDraftAsync(content.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateAndGateAsync_AutonomousFailsAfterMaxAttempts()
    {
        var content = Content.Create(ContentType.SocialPost, "Bad content");
        var profile = CreateProfile();
        SetupDbSets(contents: [content], profiles: [profile]);

        var lowJson = JsonSerializer.Serialize(new
        {
            overallScore = 40, toneAlignment = 40,
            vocabularyConsistency = 40, personaFidelity = 40,
            issues = Array.Empty<string>(),
        });
        SetupSidecarResponse(lowJson);

        _pipeline.Setup(p => p.GenerateDraftAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("Regenerated"));

        _options.MaxAutoRegenerateAttempts = 3;

        var sut = CreateSut();
        var result = await sut.ValidateAndGateAsync(content.Id, AutonomyLevel.Autonomous, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
        _pipeline.Verify(p => p.GenerateDraftAsync(content.Id, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ValidateAndGateAsync_SemiAutoReturnsAdvisoryScore()
    {
        var content = Content.Create(ContentType.SocialPost, "Content");
        var profile = CreateProfile();
        SetupDbSets(contents: [content], profiles: [profile]);

        var lowJson = JsonSerializer.Serialize(new
        {
            overallScore = 30, toneAlignment = 30,
            vocabularyConsistency = 30, personaFidelity = 30,
            issues = Array.Empty<string>(),
        });
        SetupSidecarResponse(lowJson);

        var sut = CreateSut();
        var result = await sut.ValidateAndGateAsync(content.Id, AutonomyLevel.SemiAuto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _pipeline.Verify(p => p.GenerateDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateAndGateAsync_ManualReturnsAdvisoryScore()
    {
        var content = Content.Create(ContentType.SocialPost, "Content");
        var profile = CreateProfile();
        SetupDbSets(contents: [content], profiles: [profile]);

        var lowJson = JsonSerializer.Serialize(new
        {
            overallScore = 20, toneAlignment = 20,
            vocabularyConsistency = 20, personaFidelity = 20,
            issues = Array.Empty<string>(),
        });
        SetupSidecarResponse(lowJson);

        var sut = CreateSut();
        var result = await sut.ValidateAndGateAsync(content.Id, AutonomyLevel.Manual, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _pipeline.Verify(p => p.GenerateDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
