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
}
