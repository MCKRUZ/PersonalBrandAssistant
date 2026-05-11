using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Commands;

public class CreateIdeaSourceHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_CreatesSource_WithProvidedFields()
    {
        using var db = CreateContext();
        var handler = new CreateIdeaSource.Handler(db);

        var result = await handler.Handle(
            new CreateIdeaSource.Command
            {
                Name = "Hacker News",
                Type = IdeaSourceType.RSS,
                FeedUrl = "https://hn.algolia.com/rss",
                Category = "Tech News",
                PollIntervalMinutes = 60
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var source = await db.IdeaSources.FindAsync(result.Value);
        Assert.NotNull(source);
        Assert.Equal("Hacker News", source.Name);
        Assert.Equal(IdeaSourceType.RSS, source.Type);
        Assert.Equal("https://hn.algolia.com/rss", source.FeedUrl);
        Assert.Equal("Tech News", source.Category);
        Assert.Equal(60, source.PollIntervalMinutes);
        Assert.True(source.IsEnabled);
    }

    [Fact]
    public async Task Handle_ReturnsNewId()
    {
        using var db = CreateContext();
        var handler = new CreateIdeaSource.Handler(db);

        var result = await handler.Handle(
            new CreateIdeaSource.Command { Name = "Test Source", Category = "Test" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
    }
}
