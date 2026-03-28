using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.BlogChat;

[Collection("Postgres")]
public class BlogChatFinalizationTests
{
    private readonly PostgresFixture _fixture;
    private readonly Mock<ISidecarClient> _mockSidecar = new();

    public BlogChatFinalizationTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<(BlogChatService sut, ApplicationDbContext db)> CreateSutAsync()
    {
        var db = _fixture.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var options = Options.Create(new BlogChatOptions
        {
            RecentMessageCount = 10,
            FinalizationMaxRetries = 2,
        });

        _mockSidecar.Setup(s => s.IsConnected).Returns(true);

        var sut = new BlogChatService(_mockSidecar.Object, db, options, NullLogger<BlogChatService>.Instance);
        return (sut, db);
    }

    private async Task<(Content content, ChatConversation conversation)> SeedConversationAsync(ApplicationDbContext db)
    {
        var content = Content.Create(ContentType.BlogPost, "Draft body", "Draft Title");
        db.Contents.Add(content);

        var conversation = new ChatConversation
        {
            ContentId = content.Id,
            Messages = [
                new ChatMessage("user", "Write about AI agents", DateTimeOffset.UtcNow),
                new ChatMessage("assistant", "Here's a draft about AI agents...", DateTimeOffset.UtcNow),
            ],
            LastMessageAt = DateTimeOffset.UtcNow,
        };
        db.ChatConversations.Add(conversation);
        await db.SaveChangesAsync();
        return (content, conversation);
    }

    [Fact]
    public async Task ExtractFinalDraftAsync_ReturnsSuccess_WithValidJson()
    {
        var (sut, db) = await CreateSutAsync();
        var (content, _) = await SeedConversationAsync(db);

        var validJson = """
            {
              "title": "Agent-First Enterprise",
              "subtitle": "Why AI agents change everything",
              "body_markdown": "# The Rise of Agents\n\nContent here.",
              "seo_description": "Learn how AI agents are transforming enterprise architecture.",
              "tags": ["ai", "agents", "enterprise"]
            }
            """;

        SetupSidecarResponse(validJson);

        var result = await sut.ExtractFinalDraftAsync(content.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Agent-First Enterprise", result.Value!.Title);
        Assert.Equal("Why AI agents change everything", result.Value.Subtitle);
        Assert.Contains("Rise of Agents", result.Value.BodyMarkdown);
        Assert.Equal(3, result.Value.Tags.Length);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ExtractFinalDraftAsync_SavesContentTitleAndBody()
    {
        var (sut, db) = await CreateSutAsync();
        var (content, _) = await SeedConversationAsync(db);

        var validJson = """
            {
              "title": "Updated Title",
              "subtitle": "Sub",
              "body_markdown": "Updated body content",
              "seo_description": "Description",
              "tags": ["tag1"]
            }
            """;

        SetupSidecarResponse(validJson);

        await sut.ExtractFinalDraftAsync(content.Id, default);

        var updated = await db.Contents.FirstAsync(c => c.Id == content.Id);
        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("Updated body content", updated.Body);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ExtractFinalDraftAsync_ReturnsNotFound_WhenNoConversation()
    {
        var (sut, db) = await CreateSutAsync();
        var result = await sut.ExtractFinalDraftAsync(Guid.NewGuid(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(Application.Common.Errors.ErrorCode.NotFound, result.ErrorCode);
        await db.DisposeAsync();
    }

    private void SetupSidecarResponse(string fullText)
    {
        _mockSidecar.Setup(s => s.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToSidecarEvents(fullText));
    }

    private static async IAsyncEnumerable<SidecarEvent> ToSidecarEvents(string text)
    {
        await Task.CompletedTask;
        yield return new ChatEvent("text", text, null, null);
        yield return new TaskCompleteEvent("test-session", 100, 50, 0, 0, 0.01m);
    }
}
