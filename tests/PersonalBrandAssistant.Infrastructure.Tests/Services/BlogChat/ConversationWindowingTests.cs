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
public class ConversationWindowingTests
{
    private readonly PostgresFixture _fixture;
    private readonly Mock<IClaudeChatClient> _mockClaude = new();

    public ConversationWindowingTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<(BlogChatService sut, ApplicationDbContext db)> CreateSutAsync(int recentMessageCount = 3)
    {
        var db = _fixture.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var options = Options.Create(new BlogChatOptions
        {
            RecentMessageCount = recentMessageCount,
            FinalizationMaxRetries = 2,
        });
        var sut = new BlogChatService(_mockClaude.Object, db, options, NullLogger<BlogChatService>.Instance);
        return (sut, db);
    }

    [Fact]
    public async Task Windowing_GeneratesSummary_WhenConversationExceedsThreshold()
    {
        var (sut, db) = await CreateSutAsync(recentMessageCount: 3);
        var content = Content.Create(ContentType.BlogPost, "Body", "Title");
        db.Contents.Add(content);

        var messages = new List<ChatMessage>();
        for (var i = 0; i < 8; i++)
        {
            messages.Add(new ChatMessage(i % 2 == 0 ? "user" : "assistant", $"Message {i}", DateTimeOffset.UtcNow));
        }

        var conversation = new ChatConversation
        {
            ContentId = content.Id,
            Messages = messages,
            LastMessageAt = DateTimeOffset.UtcNow,
        };
        db.ChatConversations.Add(conversation);
        await db.SaveChangesAsync();

        // Mock both stream and summary calls
        _mockClaude.Setup(c => c.StreamMessageAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ClaudeChatMessage>>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable("New response"));

        _mockClaude.Setup(c => c.SendMessageAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ClaudeChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary of earlier conversation about topics X and Y");

        await foreach (var _ in sut.SendMessageAsync(content.Id, "New message", default)) { }

        await db.Entry(conversation).ReloadAsync();
        Assert.NotNull(conversation.ConversationSummary);
        Assert.Contains("Summary", conversation.ConversationSummary);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Windowing_IncludesSummaryInClaudeRequest()
    {
        var (sut, db) = await CreateSutAsync(recentMessageCount: 3);
        var content = Content.Create(ContentType.BlogPost, "Body", "Title");
        db.Contents.Add(content);

        var conversation = new ChatConversation
        {
            ContentId = content.Id,
            Messages = [new ChatMessage("user", "Old message", DateTimeOffset.UtcNow)],
            ConversationSummary = "We discussed AI agents and their enterprise applications.",
            LastMessageAt = DateTimeOffset.UtcNow,
        };
        db.ChatConversations.Add(conversation);
        await db.SaveChangesAsync();

        IReadOnlyList<ClaudeChatMessage>? capturedMessages = null;
        _mockClaude.Setup(c => c.StreamMessageAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ClaudeChatMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, string, IReadOnlyList<ClaudeChatMessage>, CancellationToken>(
                (_, _, _, msgs, _) => capturedMessages = msgs)
            .Returns(ToAsyncEnumerable("Response"));

        await foreach (var _ in sut.SendMessageAsync(content.Id, "Continue writing", default)) { }

        Assert.NotNull(capturedMessages);
        Assert.Contains(capturedMessages, m => m.Content.Contains("Previous conversation summary"));
        await db.DisposeAsync();
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(string text)
    {
        await Task.CompletedTask;
        yield return text;
    }
}
