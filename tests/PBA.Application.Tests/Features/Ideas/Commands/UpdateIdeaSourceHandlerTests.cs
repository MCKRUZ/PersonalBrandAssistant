using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.Ideas.Commands;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.Ideas.Commands;

public class UpdateIdeaSourceHandlerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Handle_PatchesOnlyNonNullFields()
    {
        using var db = CreateContext();
        var source = new IdeaSource
        {
            Name = "Original",
            Category = "Tech",
            PollIntervalMinutes = 30,
            FeedUrl = "https://original.com/rss"
        };
        db.IdeaSources.Add(source);
        await db.SaveChangesAsync();

        var handler = new UpdateIdeaSource.Handler(db);
        var result = await handler.Handle(
            new UpdateIdeaSource.Command { Id = source.Id, Name = "Updated" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await db.IdeaSources.FindAsync(source.Id);
        Assert.Equal("Updated", updated!.Name);
        Assert.Equal("Tech", updated.Category);
        Assert.Equal(30, updated.PollIntervalMinutes);
        Assert.Equal("https://original.com/rss", updated.FeedUrl);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenSourceDoesNotExist()
    {
        using var db = CreateContext();
        var handler = new UpdateIdeaSource.Handler(db);

        var result = await handler.Handle(
            new UpdateIdeaSource.Command { Id = Guid.NewGuid(), Name = "Test" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureType.NotFound, result.FailureType);
    }
}
