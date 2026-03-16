using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;

public class TrendMonitorTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<ISidecarClient> _sidecar = new();
    private readonly Mock<ILogger<TrendMonitor>> _logger = new();
    private readonly TrendMonitoringOptions _options = new();
    private readonly List<ITrendSourcePoller> _pollers = [];

    private TrendMonitor CreateSut() => new(
        _dbContext.Object,
        _sidecar.Object,
        _pollers,
        Options.Create(_options),
        _logger.Object);

    private void SetupDbSets(
        TrendSuggestion[]? suggestions = null,
        TrendItem[]? items = null,
        TrendSource[]? sources = null,
        BrandProfile[]? profiles = null)
    {
        var suggestionMock = (suggestions ?? []).AsQueryable().BuildMockDbSet();
        suggestionMock.Setup(d => d.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns<object[], CancellationToken>((keys, _) =>
                ValueTask.FromResult((suggestions ?? []).FirstOrDefault(s => s.Id == (Guid)keys[0])));
        _dbContext.Setup(d => d.TrendSuggestions).Returns(suggestionMock.Object);

        var itemMock = (items ?? []).AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.TrendItems).Returns(itemMock.Object);

        var sourceMock = (sources ?? []).AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.TrendSources).Returns(sourceMock.Object);

        var profileMock = (profiles ?? []).AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.BrandProfiles).Returns(profileMock.Object);

        var contentList = new List<Domain.Entities.Content>();
        var contentMock = contentList.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.Contents).Returns(contentMock.Object);

        var suggestionItemList = new List<TrendSuggestionItem>();
        var suggestionItemMock = suggestionItemList.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(d => d.TrendSuggestionItems).Returns(suggestionItemMock.Object);
    }

    // --- GetSuggestionsAsync ---

    [Fact]
    public async Task GetSuggestionsAsync_WithSuggestions_ReturnsSuggestionsOrderedByRelevanceDescending()
    {
        var suggestions = new[]
        {
            new TrendSuggestion
            {
                Topic = "Low", RelevanceScore = 0.3f,
                Status = TrendSuggestionStatus.Pending, Rationale = "test",
            },
            new TrendSuggestion
            {
                Topic = "High", RelevanceScore = 0.9f,
                Status = TrendSuggestionStatus.Pending, Rationale = "test",
            },
            new TrendSuggestion
            {
                Topic = "Mid", RelevanceScore = 0.6f,
                Status = TrendSuggestionStatus.Pending, Rationale = "test",
            },
        };
        SetupDbSets(suggestions: suggestions);

        var sut = CreateSut();
        var result = await sut.GetSuggestionsAsync(10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal("High", result.Value[0].Topic);
        Assert.Equal("Mid", result.Value[1].Topic);
        Assert.Equal("Low", result.Value[2].Topic);
    }

    [Fact]
    public async Task GetSuggestionsAsync_RespectsLimit()
    {
        var suggestions = Enumerable.Range(0, 5)
            .Select(i => new TrendSuggestion
            {
                Topic = $"Topic {i}", RelevanceScore = i * 0.2f,
                Status = TrendSuggestionStatus.Pending, Rationale = "test",
            })
            .ToArray();
        SetupDbSets(suggestions: suggestions);

        var sut = CreateSut();
        var result = await sut.GetSuggestionsAsync(2, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task GetSuggestionsAsync_NoSuggestions_ReturnsEmptyList()
    {
        SetupDbSets();

        var sut = CreateSut();
        var result = await sut.GetSuggestionsAsync(10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GetSuggestionsAsync_OnlyReturnsPendingSuggestions()
    {
        var suggestions = new[]
        {
            new TrendSuggestion
            {
                Topic = "Pending", RelevanceScore = 0.9f,
                Status = TrendSuggestionStatus.Pending, Rationale = "test",
            },
            new TrendSuggestion
            {
                Topic = "Accepted", RelevanceScore = 0.8f,
                Status = TrendSuggestionStatus.Accepted, Rationale = "test",
            },
            new TrendSuggestion
            {
                Topic = "Dismissed", RelevanceScore = 0.7f,
                Status = TrendSuggestionStatus.Dismissed, Rationale = "test",
            },
        };
        SetupDbSets(suggestions: suggestions);

        var sut = CreateSut();
        var result = await sut.GetSuggestionsAsync(10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Pending", result.Value![0].Topic);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ZeroLimit_ReturnsValidationError()
    {
        SetupDbSets();

        var sut = CreateSut();
        var result = await sut.GetSuggestionsAsync(0, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    // --- DismissSuggestionAsync ---

    [Fact]
    public async Task DismissSuggestionAsync_ValidId_SetsStatusToDismissed()
    {
        var suggestion = new TrendSuggestion
        {
            Topic = "Test", RelevanceScore = 0.5f,
            Status = TrendSuggestionStatus.Pending, Rationale = "test",
        };
        SetupDbSets(suggestions: [suggestion]);

        var sut = CreateSut();
        var result = await sut.DismissSuggestionAsync(suggestion.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TrendSuggestionStatus.Dismissed, suggestion.Status);
        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DismissSuggestionAsync_InvalidId_ReturnsNotFound()
    {
        SetupDbSets();

        var sut = CreateSut();
        var result = await sut.DismissSuggestionAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // --- AcceptSuggestionAsync ---

    [Fact]
    public async Task AcceptSuggestionAsync_ValidId_SetsStatusAndCreatesContent()
    {
        var suggestion = new TrendSuggestion
        {
            Topic = "AI Trends in 2026",
            RelevanceScore = 0.9f,
            Status = TrendSuggestionStatus.Pending,
            Rationale = "Highly relevant",
            SuggestedContentType = ContentType.BlogPost,
            SuggestedPlatforms = [PlatformType.LinkedIn],
        };
        SetupDbSets(suggestions: [suggestion]);

        var sut = CreateSut();
        var result = await sut.AcceptSuggestionAsync(suggestion.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        Assert.Equal(TrendSuggestionStatus.Accepted, suggestion.Status);
        _dbContext.Verify(d => d.Contents.AddAsync(
            It.Is<Domain.Entities.Content>(c => c.Title == "AI Trends in 2026"),
            It.IsAny<CancellationToken>()), Times.Once);
        _dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptSuggestionAsync_AlreadyAccepted_ReturnsConflict()
    {
        var suggestion = new TrendSuggestion
        {
            Topic = "Already Done",
            RelevanceScore = 0.9f,
            Status = TrendSuggestionStatus.Accepted,
            Rationale = "test",
        };
        SetupDbSets(suggestions: [suggestion]);

        var sut = CreateSut();
        var result = await sut.AcceptSuggestionAsync(suggestion.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Conflict, result.ErrorCode);
    }

    [Fact]
    public async Task AcceptSuggestionAsync_InvalidId_ReturnsNotFound()
    {
        SetupDbSets();

        var sut = CreateSut();
        var result = await sut.AcceptSuggestionAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // --- ParseRelevanceScores ---

    [Fact]
    public void ParseRelevanceScores_ValidJson_ReturnsScores()
    {
        var json = """[{"index": 0, "score": 0.85}, {"index": 1, "score": 0.4}]""";
        var scores = TrendMonitor.ParseRelevanceScores(json);

        Assert.Equal(2, scores.Count);
        Assert.Equal(0, scores[0].Index);
        Assert.Equal(0.85f, scores[0].Score);
    }

    [Fact]
    public void ParseRelevanceScores_MarkdownFenced_StripsAndParses()
    {
        var json = """
            ```json
            [{"index": 0, "score": 0.7}]
            ```
            """;
        var scores = TrendMonitor.ParseRelevanceScores(json);

        Assert.Single(scores);
        Assert.Equal(0.7f, scores[0].Score);
    }

    [Fact]
    public void ParseRelevanceScores_InvalidJson_ReturnsEmpty()
    {
        var scores = TrendMonitor.ParseRelevanceScores("I think the score is about 7");
        Assert.Empty(scores);
    }
}
