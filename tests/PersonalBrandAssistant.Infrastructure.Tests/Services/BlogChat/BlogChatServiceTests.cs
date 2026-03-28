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
public class BlogChatServiceTests
{
    private readonly PostgresFixture _fixture;
    private readonly Mock<ISidecarClient> _mockSidecar = new();

    public BlogChatServiceTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<(BlogChatService sut, ApplicationDbContext db)> CreateSutAsync()
    {
        var db = _fixture.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var options = Options.Create(new BlogChatOptions
        {
            RecentMessageCount = 5,
            FinalizationMaxRetries = 2,
        });

        _mockSidecar.Setup(s => s.IsConnected).Returns(true);

        var sut = new BlogChatService(_mockSidecar.Object, db, options, NullLogger<BlogChatService>.Instance);
        return (sut, db);
    }

    private static Content CreateBlogContent()
    {
        return Content.Create(ContentType.BlogPost, "Initial body", "Test Blog");
    }

    [Fact]
    public async Task SendMessageAsync_CreatesNewConversation_OnFirstMessage()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        SetupSidecarResponse("Hello! Let me help you write.");

        var chunks = new List<string>();
        await foreach (var chunk in sut.SendMessageAsync(content.Id, "Help me write a blog post", default))
            chunks.Add(chunk);

        var conversation = await db.ChatConversations.FirstOrDefaultAsync(c => c.ContentId == content.Id);
        Assert.NotNull(conversation);
        Assert.Equal(2, conversation.Messages.Count);
        Assert.Equal("user", conversation.Messages[0].Role);
        Assert.Equal("assistant", conversation.Messages[1].Role);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SendMessageAsync_AppendsToExisting_OnSubsequentMessages()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);

        var conversation = new ChatConversation
        {
            ContentId = content.Id,
            Messages = [new ChatMessage("user", "First msg", DateTimeOffset.UtcNow),
                        new ChatMessage("assistant", "First reply", DateTimeOffset.UtcNow)],
            LastMessageAt = DateTimeOffset.UtcNow,
        };
        db.ChatConversations.Add(conversation);
        await db.SaveChangesAsync();

        SetupSidecarResponse("Second reply");

        await foreach (var _ in sut.SendMessageAsync(content.Id, "Second msg", default)) { }

        await db.Entry(conversation).ReloadAsync();
        Assert.Equal(4, conversation.Messages.Count);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SendMessageAsync_PersistsAssistantMessage_OnlyAfterStreamCompletes()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        SetupSidecarResponse("Full response text");

        var allChunks = new List<string>();
        await foreach (var chunk in sut.SendMessageAsync(content.Id, "Write something", default))
            allChunks.Add(chunk);

        var conv = await db.ChatConversations.FirstAsync(c => c.ContentId == content.Id);
        var assistantMsg = conv.Messages.Last();
        Assert.Equal("assistant", assistantMsg.Role);
        Assert.Equal("Full response text", assistantMsg.Content);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetConversationAsync_ReturnsNull_ForContentWithNoConversation()
    {
        var (sut, db) = await CreateSutAsync();
        var result = await sut.GetConversationAsync(Guid.NewGuid(), default);
        Assert.Null(result);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetConversationAsync_ReturnsConversation_WithAllMessages()
    {
        var (sut, db) = await CreateSutAsync();
        var content = CreateBlogContent();
        db.Contents.Add(content);

        var conversation = new ChatConversation
        {
            ContentId = content.Id,
            Messages = [new ChatMessage("user", "Hello", DateTimeOffset.UtcNow),
                        new ChatMessage("assistant", "Hi there", DateTimeOffset.UtcNow),
                        new ChatMessage("user", "Write about AI", DateTimeOffset.UtcNow)],
            LastMessageAt = DateTimeOffset.UtcNow,
        };
        db.ChatConversations.Add(conversation);
        await db.SaveChangesAsync();

        var result = await sut.GetConversationAsync(content.Id, default);
        Assert.NotNull(result);
        Assert.Equal(3, result.Messages.Count);
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
