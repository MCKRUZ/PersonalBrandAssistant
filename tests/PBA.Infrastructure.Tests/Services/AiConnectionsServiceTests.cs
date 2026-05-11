using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Services;
using Xunit;

namespace PBA.Infrastructure.Tests.Services;

public class AiConnectionsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ISidecarClient> _sidecarMock = new();
    private readonly Mock<ILogger<AiConnectionsService>> _loggerMock = new();

    public AiConnectionsServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(dbOptions);
    }

    private AiConnectionsService CreateService()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(_dbContext);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new AiConnectionsService(
            scopeFactory.Object,
            _sidecarMock.Object,
            _loggerMock.Object);
    }

    private void SeedIdeas(int count, IdeaStatus status = IdeaStatus.Saved)
    {
        for (var i = 0; i < count; i++)
        {
            _dbContext.Ideas.Add(new Idea
            {
                Title = $"Idea {i + 1}",
                Description = $"Description {i + 1}",
                Url = $"https://example.com/{i + 1}",
                Tags = ["tag1", "tag2"],
                Status = status,
                DetectedAt = DateTimeOffset.UtcNow.AddHours(-i),
                DeduplicationKey = $"key-{i}",
            });
        }
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task AnalyzeConnectionsAsync_SkipsWhenNoSavedIdeas()
    {
        SeedIdeas(3, IdeaStatus.New);
        var service = CreateService();

        await service.AnalyzeConnectionsAsync(CancellationToken.None);

        _sidecarMock.Verify(
            s => s.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnalyzeConnectionsAsync_LimitsTo50MostRecentIdeas()
    {
        SeedIdeas(60, IdeaStatus.Saved);
        string? capturedPrompt = null;
        _sidecarMock.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, user, _) => capturedPrompt = user)
            .ReturnsAsync("[]");

        var service = CreateService();
        await service.AnalyzeConnectionsAsync(CancellationToken.None);

        Assert.NotNull(capturedPrompt);
        var ideaCount = capturedPrompt!.Split("[id:").Length - 1;
        Assert.Equal(50, ideaCount);
    }

    [Fact]
    public async Task AnalyzeConnectionsAsync_IncludesTitleDescriptionUrlTags()
    {
        _dbContext.Ideas.Add(new Idea
        {
            Title = "AI Governance Framework",
            Description = "Comparing frameworks",
            Url = "https://example.com/ai-governance",
            Tags = ["ai", "governance"],
            Status = IdeaStatus.Saved,
            DeduplicationKey = "k1",
        });
        _dbContext.SaveChanges();

        string? capturedPrompt = null;
        _sidecarMock.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, user, _) => capturedPrompt = user)
            .ReturnsAsync("[]");

        var service = CreateService();
        await service.AnalyzeConnectionsAsync(CancellationToken.None);

        Assert.Contains("AI Governance Framework", capturedPrompt!);
        Assert.Contains("Comparing frameworks", capturedPrompt);
        Assert.Contains("https://example.com/ai-governance", capturedPrompt);
        Assert.Contains("ai", capturedPrompt);
        Assert.Contains("governance", capturedPrompt);
    }

    [Fact]
    public async Task AnalyzeConnectionsAsync_ParsesValidJsonAndUpdatesIdeas()
    {
        var idea1 = new Idea { Title = "Idea A", Status = IdeaStatus.Saved, DeduplicationKey = "k1" };
        var idea2 = new Idea { Title = "Idea B", Status = IdeaStatus.Saved, DeduplicationKey = "k2" };
        _dbContext.Ideas.AddRange(idea1, idea2);
        _dbContext.SaveChanges();

        var responseJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                theme = "AI Trends",
                ideaIds = new[] { idea1.Id.ToString(), idea2.Id.ToString() },
                suggestedAngle = "Compare frameworks",
                confidence = 0.85,
            },
        });

        _sidecarMock.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseJson);

        var service = CreateService();
        await service.AnalyzeConnectionsAsync(CancellationToken.None);

        var updated1 = await _dbContext.Ideas.FindAsync(idea1.Id);
        var updated2 = await _dbContext.Ideas.FindAsync(idea2.Id);
        Assert.NotNull(updated1!.AIConnections);
        Assert.NotNull(updated2!.AIConnections);
        Assert.Contains("AI Trends", updated1.AIConnections);
        Assert.StartsWith("[", updated1.AIConnections);
    }

    [Fact]
    public async Task AnalyzeConnectionsAsync_StripsMarkdownFences()
    {
        var idea = new Idea { Title = "Test", Status = IdeaStatus.Saved, DeduplicationKey = "k1" };
        _dbContext.Ideas.Add(idea);
        _dbContext.SaveChanges();

        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                theme = "Fenced",
                ideaIds = new[] { idea.Id.ToString() },
                suggestedAngle = "Test angle",
                confidence = 0.9,
            },
        });
        var fencedResponse = $"```json\n{json}\n```";

        _sidecarMock.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fencedResponse);

        var service = CreateService();
        await service.AnalyzeConnectionsAsync(CancellationToken.None);

        var updated = await _dbContext.Ideas.FindAsync(idea.Id);
        Assert.NotNull(updated!.AIConnections);
        Assert.Contains("Fenced", updated.AIConnections);
    }

    [Fact]
    public async Task AnalyzeConnectionsAsync_LogsWarningOnParseFailure()
    {
        SeedIdeas(2);
        _sidecarMock.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("I can't process that request");

        var service = CreateService();
        await service.AnalyzeConnectionsAsync(CancellationToken.None);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        var ideas = await _dbContext.Ideas.ToListAsync();
        Assert.All(ideas, i => Assert.Null(i.AIConnections));
    }

    [Fact]
    public async Task AnalyzeConnectionsAsync_SkipsUnknownIdeaIds()
    {
        var idea = new Idea { Title = "Known", Status = IdeaStatus.Saved, DeduplicationKey = "k1" };
        _dbContext.Ideas.Add(idea);
        _dbContext.SaveChanges();

        var responseJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                theme = "Test",
                ideaIds = new[] { idea.Id.ToString(), Guid.NewGuid().ToString() },
                suggestedAngle = "Angle",
                confidence = 0.7,
            },
        });

        _sidecarMock.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseJson);

        var service = CreateService();
        await service.AnalyzeConnectionsAsync(CancellationToken.None);

        var updated = await _dbContext.Ideas.FindAsync(idea.Id);
        Assert.NotNull(updated!.AIConnections);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
