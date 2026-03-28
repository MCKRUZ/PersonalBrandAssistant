using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public class BlogChatService : IBlogChatService
{
    private readonly IClaudeChatClient _claude;
    private readonly IApplicationDbContext _db;
    private readonly BlogChatOptions _options;
    private readonly ILogger<BlogChatService> _logger;
    private readonly string _systemPrompt;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public BlogChatService(
        IClaudeChatClient claude,
        IApplicationDbContext db,
        IOptions<BlogChatOptions> options,
        ILogger<BlogChatService> logger)
    {
        _claude = claude;
        _db = db;
        _options = options.Value;
        _logger = logger;
        _systemPrompt = LoadSystemPrompt(_options.SystemPromptPath);
    }

    public async IAsyncEnumerable<string> SendMessageAsync(
        Guid contentId, string userMessage, [EnumeratorCancellation] CancellationToken ct)
    {
        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.ContentId == contentId, ct);

        if (conversation is null)
        {
            conversation = new ChatConversation
            {
                ContentId = contentId,
                Messages = [],
                LastMessageAt = DateTimeOffset.UtcNow,
            };
            _db.ChatConversations.Add(conversation);
        }

        conversation.Messages = [..conversation.Messages, new ChatMessage("user", userMessage, DateTimeOffset.UtcNow)];
        conversation.LastMessageAt = DateTimeOffset.UtcNow;
        MarkMessagesModified(conversation);
        await _db.SaveChangesAsync(ct);

        var claudeMessages = BuildClaudeMessages(conversation);
        var fullResponse = new StringBuilder();

        await foreach (var chunk in _claude.StreamMessageAsync(
            _options.Model, _options.MaxTokens, _systemPrompt, claudeMessages, ct))
        {
            fullResponse.Append(chunk);
            yield return chunk;
        }

        conversation.Messages = [..conversation.Messages, new ChatMessage("assistant", fullResponse.ToString(), DateTimeOffset.UtcNow)];
        conversation.LastMessageAt = DateTimeOffset.UtcNow;
        MarkMessagesModified(conversation);

        if (conversation.Messages.Count > _options.RecentMessageCount * 2)
            await UpdateConversationSummaryAsync(conversation, ct);

        await _db.SaveChangesAsync(ct);
    }

    public async Task<ChatConversation?> GetConversationAsync(Guid contentId, CancellationToken ct)
    {
        return await _db.ChatConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ContentId == contentId, ct);
    }

    public async Task<Result<FinalizedDraft>> ExtractFinalDraftAsync(Guid contentId, CancellationToken ct)
    {
        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.ContentId == contentId, ct);

        if (conversation is null)
            return Result<FinalizedDraft>.Failure(ErrorCode.NotFound, "No conversation found for this content");

        var claudeMessages = BuildClaudeMessages(conversation);
        claudeMessages.Add(new ClaudeChatMessage("user", FinalizationPrompt));

        for (var attempt = 0; attempt <= _options.FinalizationMaxRetries; attempt++)
        {
            try
            {
                var response = await _claude.SendMessageAsync(
                    _options.Model, _options.MaxTokens, _systemPrompt, claudeMessages, ct);

                var jsonStart = response.IndexOf('{');
                var jsonEnd = response.LastIndexOf('}');
                if (jsonStart < 0 || jsonEnd < 0)
                {
                    claudeMessages.Add(new ClaudeChatMessage("assistant", response));
                    claudeMessages.Add(new ClaudeChatMessage("user",
                        "Your response did not contain valid JSON. Please respond with ONLY the JSON object, no markdown fences."));
                    continue;
                }

                var json = response[jsonStart..(jsonEnd + 1)];
                var draft = JsonSerializer.Deserialize<FinalizedDraftJson>(json, JsonOptions);

                if (draft is null || string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.BodyMarkdown))
                {
                    claudeMessages.Add(new ClaudeChatMessage("assistant", response));
                    claudeMessages.Add(new ClaudeChatMessage("user",
                        "JSON was missing required fields (title, body_markdown). Please include all fields."));
                    continue;
                }

                var finalized = new FinalizedDraft(
                    draft.Title, draft.Subtitle ?? "", draft.BodyMarkdown,
                    draft.SeoDescription ?? "", draft.Tags ?? []);

                var content = await _db.Contents.FirstOrDefaultAsync(c => c.Id == contentId, ct);
                if (content is not null)
                {
                    content.Title = finalized.Title;
                    content.Body = finalized.BodyMarkdown;
                    await _db.SaveChangesAsync(ct);
                }

                return Result<FinalizedDraft>.Success(finalized);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse finalization JSON (attempt {Attempt})", attempt + 1);
                claudeMessages.Add(new ClaudeChatMessage("user",
                    $"JSON parsing error: {ex.Message}. Please respond with valid JSON only."));
            }
        }

        return Result<FinalizedDraft>.Failure(ErrorCode.InternalError, "Failed to extract final draft after retries");
    }

    private void MarkMessagesModified(ChatConversation conversation)
    {
        if (_db is Microsoft.EntityFrameworkCore.DbContext dbContext)
            dbContext.Entry(conversation).Property(c => c.Messages).IsModified = true;
    }

    private List<ClaudeChatMessage> BuildClaudeMessages(ChatConversation conversation)
    {
        var messages = new List<ClaudeChatMessage>();

        if (!string.IsNullOrWhiteSpace(conversation.ConversationSummary))
        {
            messages.Add(new ClaudeChatMessage("user",
                $"[Previous conversation summary]: {conversation.ConversationSummary}"));
            messages.Add(new ClaudeChatMessage("assistant",
                "Understood, I have context from our previous conversation. Let's continue."));
        }

        var recentMessages = conversation.Messages
            .TakeLast(_options.RecentMessageCount)
            .Select(m => new ClaudeChatMessage(m.Role, m.Content))
            .ToList();

        messages.AddRange(recentMessages);
        return messages;
    }

    private async Task UpdateConversationSummaryAsync(ChatConversation conversation, CancellationToken ct)
    {
        var olderMessages = conversation.Messages
            .Take(conversation.Messages.Count - _options.RecentMessageCount)
            .Select(m => $"{m.Role}: {m.Content}")
            .ToList();

        if (olderMessages.Count == 0) return;

        var summaryPrompt = new List<ClaudeChatMessage>
        {
            new("user", $"Summarize this conversation concisely, preserving key decisions and content direction:\n\n{string.Join("\n\n", olderMessages)}")
        };

        try
        {
            var summary = await _claude.SendMessageAsync(
                _options.Model, 1024, "You are a conversation summarizer. Be concise.", summaryPrompt, ct);
            conversation.ConversationSummary = summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate conversation summary");
        }
    }

    private static string LoadSystemPrompt(string path)
    {
        if (File.Exists(path))
            return File.ReadAllText(path);

        return DefaultSystemPrompt;
    }

    private const string DefaultSystemPrompt = """
        You are a blog writing assistant for Matt Kruczek, an enterprise AI thought leader.
        Help craft blog posts that are direct, technically grounded, and avoid AI slop.
        Never use em dashes. Write in Matt's authentic voice: developer-to-executive authority.
        Focus on practical enterprise AI insights, not hype.
        """;

    private const string FinalizationPrompt = """
        Based on our conversation, extract the final blog post as a JSON object with these exact fields:
        {
          "title": "The blog post title",
          "subtitle": "A subtitle or tagline",
          "body_markdown": "The full blog post body in markdown",
          "seo_description": "A 150-160 character SEO meta description",
          "tags": ["tag1", "tag2", "tag3"]
        }
        Respond with ONLY the JSON object, no markdown fences or explanation.
        """;

    private record FinalizedDraftJson(
        string Title, string? Subtitle, string BodyMarkdown,
        string? SeoDescription, string[]? Tags);
}
