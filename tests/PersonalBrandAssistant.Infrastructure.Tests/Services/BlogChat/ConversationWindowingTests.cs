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
    private readonly Mock<ISidecarClient> _mockSidecar = new();

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

        _mockSidecar.Setup(s => s.IsConnected).Returns(true);

        var sut = new BlogChatService(_mockSidecar.Object, db, options, NullLogger<BlogChatService>.Instance);
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

        // First call = chat response, second call = summary generation
        var callCount = 0;
        _mockSidecar.Setup(s => s.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                return callCount == 1
                    ? ToSidecarEvents("New response")
                    : ToSidecarEvents("Summary of earlier conversation about topics X and Y");
            });

        await foreach (var _ in sut.SendMessageAsync(content.Id, "New message", default)) { }

        await db.Entry(conversation).ReloadAsync();
        Assert.NotNull(conversation.ConversationSummary);
        Assert.Contains("Summary", conversation.ConversationSummary);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Windowing_IncludesSummaryInContext()
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

        string? capturedTask = null;
        _mockSidecar.Setup(s => s.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, string?, CancellationToken>(
                (task, _, _, _) => capturedTask = task)
            .Returns(ToSidecarEvents("Response"));

        await foreach (var _ in sut.SendMessageAsync(content.Id, "Continue writing", default)) { }

        Assert.NotNull(capturedTask);
        Assert.Contains("Previous conversation summary", capturedTask);
        await db.DisposeAsync();
    }

    private static async IAsyncEnumerable<SidecarEvent> ToSidecarEvents(string text)
    {
        await Task.CompletedTask;
        yield return new ChatEvent("text", text, null, null);
        yield return new TaskCompleteEvent("test-session", 100, 50, 0, 0, 0.01m);
    }
}
