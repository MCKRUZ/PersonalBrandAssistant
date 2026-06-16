using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Queries;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Queries;

public class ListIdeasHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Idea CreateIdea(
        string title = "Test Idea",
        IdeaStatus status = IdeaStatus.New,
        Guid? ideaSourceId = null,
        string? category = null,
        DateTimeOffset? detectedAt = null,
        string? description = null,
        string? summary = null)
    {
        return new Idea
        {
            Title = title,
            Status = status,
            IdeaSourceId = ideaSourceId,
            Category = category,
            DetectedAt = detectedAt ?? DateTimeOffset.UtcNow,
            Description = description,
            Summary = summary,
            DeduplicationKey = Guid.NewGuid().ToString(),
            SourceName = "test-source"
        };
    }

    [Fact]
    public async Task Handle_DefaultQuery_ReturnsPaginatedResults()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 25; i++)
            context.Ideas.Add(CreateIdea($"Idea {i}"));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value!.Items.Count);
        Assert.Equal(25, result.Value.TotalCount);
        Assert.Equal(2, result.Value.TotalPages);
    }

    [Fact]
    public async Task Handle_Page2_ReturnsRemainingItems()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 25; i++)
            context.Ideas.Add(CreateIdea($"Idea {i}"));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { Page = 2, PageSize = 20 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Items.Count);
        Assert.Equal(25, result.Value.TotalCount);
    }

    [Fact]
    public async Task Handle_StatusFilter_ReturnsMatchingOnly()
    {
        await using var context = CreateContext();
        context.Ideas.Add(CreateIdea("New Idea", IdeaStatus.New));
        context.Ideas.Add(CreateIdea("Saved Idea", IdeaStatus.Saved));
        context.Ideas.Add(CreateIdea("Used Idea", IdeaStatus.Used));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { Status = IdeaStatus.Saved },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Saved Idea", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task Handle_SourceFilter_ReturnsMatchingOnly()
    {
        await using var context = CreateContext();
        var sourceId = Guid.NewGuid();
        var source = new IdeaSource { Id = sourceId, Name = "Test Source" };
        context.IdeaSources.Add(source);
        context.Ideas.Add(CreateIdea("With Source", ideaSourceId: sourceId));
        context.Ideas.Add(CreateIdea("No Source"));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { IdeaSourceId = sourceId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("With Source", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task Handle_CategoryFilter_CaseInsensitivePartialMatch()
    {
        await using var context = CreateContext();
        context.Ideas.Add(CreateIdea("AI Post", category: "Artificial Intelligence"));
        context.Ideas.Add(CreateIdea("Dev Post", category: "Development"));
        context.Ideas.Add(CreateIdea("AI Dev Post", category: "AI Development"));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { Category = "development" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task Handle_DateRangeFilter_ReturnsWithinRange()
    {
        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        context.Ideas.Add(CreateIdea("Old", detectedAt: now.AddDays(-10)));
        context.Ideas.Add(CreateIdea("InRange", detectedAt: now.AddDays(-3)));
        context.Ideas.Add(CreateIdea("Future", detectedAt: now.AddDays(5)));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { DateFrom = now.AddDays(-5), DateTo = now },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("InRange", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task Handle_SearchText_MatchesTitleDescriptionSummary()
    {
        await using var context = CreateContext();
        context.Ideas.Add(CreateIdea("Claude AI Tips", description: "General tips"));
        context.Ideas.Add(CreateIdea("Other Post", description: "Claude is great", summary: "summary"));
        context.Ideas.Add(CreateIdea("Nothing Here", description: "unrelated", summary: "nope"));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { SearchText = "claude" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task Handle_MultipleFilters_CombineWithAnd()
    {
        await using var context = CreateContext();
        context.Ideas.Add(CreateIdea("Match Both", status: IdeaStatus.New, category: "AI"));
        context.Ideas.Add(CreateIdea("Status Only", status: IdeaStatus.New, category: "Dev"));
        context.Ideas.Add(CreateIdea("Category Only", status: IdeaStatus.Saved, category: "AI"));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { Status = IdeaStatus.New, Category = "AI" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Match Both", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task Handle_DefaultSort_OrdersByDetectedAtDescending()
    {
        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        context.Ideas.Add(CreateIdea("Oldest", detectedAt: now.AddDays(-3)));
        context.Ideas.Add(CreateIdea("Newest", detectedAt: now));
        context.Ideas.Add(CreateIdea("Middle", detectedAt: now.AddDays(-1)));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Newest", result.Value!.Items[0].Title);
        Assert.Equal("Middle", result.Value.Items[1].Title);
        Assert.Equal("Oldest", result.Value.Items[2].Title);
    }

    [Fact]
    public async Task Handle_TitleSortAscending_OrdersByTitle()
    {
        await using var context = CreateContext();
        context.Ideas.Add(CreateIdea("Charlie"));
        context.Ideas.Add(CreateIdea("Alpha"));
        context.Ideas.Add(CreateIdea("Bravo"));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { SortBy = "title", SortDirection = "asc" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Alpha", result.Value!.Items[0].Title);
        Assert.Equal("Bravo", result.Value.Items[1].Title);
        Assert.Equal("Charlie", result.Value.Items[2].Title);
    }

    [Fact]
    public async Task Handle_EmptyDatabase_ReturnsEmptyPage()
    {
        await using var context = CreateContext();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task Handle_PaginationOffset_SkipsCorrectly()
    {
        await using var context = CreateContext();
        for (var i = 0; i < 10; i++)
            context.Ideas.Add(CreateIdea($"Idea {i}"));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { Page = 2, PageSize = 3 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
        Assert.Equal(10, result.Value.TotalCount);
        Assert.Equal(4, result.Value.TotalPages);
    }

    [Fact]
    public async Task Handle_SortByScore_OrdersDescending()
    {
        await using var context = CreateContext();
        context.Ideas.Add(new Idea { Title = "Score3", Score = 3, DeduplicationKey = Guid.NewGuid().ToString(), SourceName = "test-source" });
        context.Ideas.Add(new Idea { Title = "Score9", Score = 9, DeduplicationKey = Guid.NewGuid().ToString(), SourceName = "test-source" });
        context.Ideas.Add(new Idea { Title = "Score6", Score = 6, DeduplicationKey = Guid.NewGuid().ToString(), SourceName = "test-source" });
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { SortBy = "score", SortDirection = "desc" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
        Assert.Equal("Score9", result.Value.Items[0].Title);
        Assert.Equal("Score6", result.Value.Items[1].Title);
        Assert.Equal("Score3", result.Value.Items[2].Title);
    }

    [Fact]
    public async Task Handle_MinScoreFilter_ExcludesBelowThreshold()
    {
        await using var context = CreateContext();
        context.Ideas.Add(new Idea { Title = "Low", Score = 3, DeduplicationKey = Guid.NewGuid().ToString(), SourceName = "test-source" });
        context.Ideas.Add(new Idea { Title = "High", Score = 8, DeduplicationKey = Guid.NewGuid().ToString(), SourceName = "test-source" });
        context.Ideas.Add(new Idea { Title = "Mid", Score = 6, DeduplicationKey = Guid.NewGuid().ToString(), SourceName = "test-source" });
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { MinScore = 6 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.DoesNotContain(result.Value.Items, i => i.Title == "Low");
    }

    [Fact]
    public async Task Handle_DefaultQuery_ExcludesDuplicates()
    {
        await using var context = CreateContext();
        var originalId = Guid.NewGuid();
        context.Ideas.Add(new Idea { Id = originalId, Title = "Original", DeduplicationKey = Guid.NewGuid().ToString(), SourceName = "test-source" });
        context.Ideas.Add(new Idea { Title = "Duplicate", DuplicateOfId = originalId, DeduplicationKey = Guid.NewGuid().ToString(), SourceName = "test-source" });
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Original", result.Value.Items[0].Title);
        Assert.False(result.Value.Items[0].IsDuplicate);
    }

    [Fact]
    public async Task Handle_IncludeDuplicatesTrue_ReturnsDuplicates()
    {
        await using var context = CreateContext();
        var originalId = Guid.NewGuid();
        context.Ideas.Add(new Idea { Id = originalId, Title = "Original", DeduplicationKey = Guid.NewGuid().ToString(), SourceName = "test-source" });
        context.Ideas.Add(new Idea { Title = "Duplicate", DuplicateOfId = originalId, DeduplicationKey = Guid.NewGuid().ToString(), SourceName = "test-source" });
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(
            new ListIdeas.Query { IncludeDuplicates = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.IsDuplicate);
        Assert.Contains(result.Value.Items, i => !i.IsDuplicate);
    }

    // --- Section-08: brand-anchored rank ----------------------------------------------------------

    private static readonly Guid Pillar1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static void AddActiveProfile(ApplicationDbContext context, int version = 1, double weight = 1.0)
    {
        context.BrandRankingProfiles.Add(new BrandRankingProfile
        {
            Version = version, IsActive = true, UpdatedAt = DateTimeOffset.UtcNow,
            Positioning = "P", AudiencePrimary = "A",
            HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
            Pillars = [new BrandPillar { Id = Pillar1, Name = "Pillar One", Description = "d", Weight = weight, Order = 0 }]
        });
    }

    private static Idea Scored(string title, double subScore, DateTimeOffset? detectedAt = null,
        int scoredVersion = 1, bool? anti = null, bool? authority = null) => new()
    {
        Title = title, SourceName = "test-source", DeduplicationKey = Guid.NewGuid().ToString(),
        Status = IdeaStatus.New, DetectedAt = detectedAt ?? DateTimeOffset.UtcNow,
        ScoredProfileVersion = scoredVersion, IsAntiTopic = anti, IsAuthorityTopic = authority,
        PillarSubScores = [new PillarSubScore { PillarId = Pillar1, PillarName = "Pillar One", Score = subScore, Reason = "fits" }]
    };

    [Fact]
    public async Task Handle_DefaultSort_IsRank_OrdersByCompositeRankDescending()
    {
        await using var context = CreateContext();
        AddActiveProfile(context);
        context.Ideas.Add(Scored("Low", 0.2));
        context.Ideas.Add(Scored("High", 0.9));
        context.Ideas.Add(Scored("Mid", 0.5));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None); // no SortBy

        Assert.True(result.IsSuccess);
        Assert.Equal("High", result.Value!.Items[0].Title);
        Assert.Equal("Mid", result.Value.Items[1].Title);
        Assert.Equal("Low", result.Value.Items[2].Title);
    }

    [Fact]
    public async Task Handle_ActiveProfile_PopulatesRankFieldsAndBreakdown()
    {
        await using var context = CreateContext();
        AddActiveProfile(context);
        context.Ideas.Add(Scored("Item", 0.8, authority: true));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);

        var dto = Assert.Single(result.Value!.Items);
        Assert.Equal(0.8, dto.BrandFit, 6);
        Assert.True(dto.Rank > 0);
        Assert.True(dto.RecencyFactor > 0);
        Assert.True(dto.IsAuthorityTopic);
        var breakdown = Assert.Single(dto.PillarBreakdown);
        Assert.Equal("Pillar One", breakdown.Name);
        Assert.Equal(0.8, breakdown.Score, 6);
    }

    [Fact]
    public async Task Handle_IdeaScoredAtOlderVersion_SurfacesStale()
    {
        await using var context = CreateContext();
        AddActiveProfile(context, version: 2);
        context.Ideas.Add(Scored("Stale", 0.5, scoredVersion: 1));
        context.Ideas.Add(Scored("Fresh", 0.5, scoredVersion: 2));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);

        Assert.True(result.Value!.Items.Single(i => i.Title == "Stale").Stale);
        Assert.False(result.Value.Items.Single(i => i.Title == "Fresh").Stale);
    }

    [Fact]
    public async Task Handle_IdeaWithEmbedding_RanksWithoutSelectingTheVector()
    {
        await using var context = CreateContext();
        AddActiveProfile(context);
        var idea = Scored("Embedded", 0.7);
        idea.Embedding = new float[] { 0.1f, 0.2f }; // present in the row, must NOT break the projection (R-C1a)
        context.Ideas.Add(idea);
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);

        var dto = Assert.Single(result.Value!.Items);
        Assert.Equal(0.7, dto.BrandFit, 6); // ranked fine; IdeaDto has no embedding field by construction
    }

    [Fact]
    public async Task Handle_NoActiveProfile_RanksZero_AndFallsBackToRecencyOrder()
    {
        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        context.Ideas.Add(Scored("Older", 0.9, detectedAt: now.AddDays(-2)));
        context.Ideas.Add(Scored("Newer", 0.1, detectedAt: now));
        await context.SaveChangesAsync();

        var handler = new ListIdeas.Handler(context);
        var result = await handler.Handle(new ListIdeas.Query(), CancellationToken.None);

        Assert.All(result.Value!.Items, i => Assert.Equal(0, i.Rank));
        Assert.Equal("Newer", result.Value.Items[0].Title); // rank-tie -> DetectedAt desc
    }
}
