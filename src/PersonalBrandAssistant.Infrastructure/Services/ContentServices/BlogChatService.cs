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
    private readonly ISidecarClient _sidecar;
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
        ISidecarClient sidecar,
        IApplicationDbContext db,
        IOptions<BlogChatOptions> options,
        ILogger<BlogChatService> logger)
    {
        _sidecar = sidecar;
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

        var contextMessages = BuildContextString(conversation);
        var task = $"{contextMessages}\n\nUser: {userMessage}";
        var fullResponse = new StringBuilder();

        if (!_sidecar.IsConnected)
            await _sidecar.ConnectAsync(ct);

        await foreach (var evt in _sidecar.SendTaskAsync(task, _systemPrompt, null, ct))
        {
            if (evt is ChatEvent chatEvt && chatEvt.Text is not null)
            {
                fullResponse.Append(chatEvt.Text);
                yield return chatEvt.Text;
            }
            else if (evt is ErrorEvent errorEvt)
            {
                _logger.LogWarning("Sidecar error during chat: {Error}", errorEvt.Message);
                break;
            }
            else if (evt is TaskCompleteEvent)
            {
                break;
            }
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

        var contextMessages = BuildContextString(conversation);
        var finalizationTask = $"{contextMessages}\n\n{FinalizationPrompt}";

        for (var attempt = 0; attempt <= _options.FinalizationMaxRetries; attempt++)
        {
            try
            {
                if (!_sidecar.IsConnected)
                    await _sidecar.ConnectAsync(ct);

                var responseSb = new StringBuilder();
                await foreach (var evt in _sidecar.SendTaskAsync(finalizationTask, _systemPrompt, null, ct))
                {
                    if (evt is ChatEvent chatEvt && chatEvt.Text is not null)
                        responseSb.Append(chatEvt.Text);
                    else if (evt is TaskCompleteEvent)
                        break;
                }
                var response = responseSb.ToString();

                var jsonStart = response.IndexOf('{');
                var jsonEnd = response.LastIndexOf('}');
                if (jsonStart < 0 || jsonEnd < 0)
                {
                    finalizationTask = $"{response}\n\nYour response did not contain valid JSON. Please respond with ONLY the JSON object, no markdown fences.";
                    continue;
                }

                var json = response[jsonStart..(jsonEnd + 1)];
                var draft = JsonSerializer.Deserialize<FinalizedDraftJson>(json, JsonOptions);

                if (draft is null || string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.BodyMarkdown))
                {
                    finalizationTask = $"{response}\n\nJSON was missing required fields (title, body_markdown). Please include all fields.";
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
                finalizationTask = $"JSON parsing error: {ex.Message}. Please respond with valid JSON only.";
            }
        }

        return Result<FinalizedDraft>.Failure(ErrorCode.InternalError, "Failed to extract final draft after retries");
    }

    private void MarkMessagesModified(ChatConversation conversation)
    {
        if (_db is Microsoft.EntityFrameworkCore.DbContext dbContext)
            dbContext.Entry(conversation).Property(c => c.Messages).IsModified = true;
    }

    private string BuildContextString(ChatConversation conversation)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(conversation.ConversationSummary))
            sb.AppendLine($"[Previous conversation summary]: {conversation.ConversationSummary}\n");

        var recentMessages = conversation.Messages
            .TakeLast(_options.RecentMessageCount);

        foreach (var msg in recentMessages)
            sb.AppendLine($"{(msg.Role == "user" ? "User" : "Assistant")}: {msg.Content}\n");

        return sb.ToString();
    }

    private async Task UpdateConversationSummaryAsync(ChatConversation conversation, CancellationToken ct)
    {
        var olderMessages = conversation.Messages
            .Take(conversation.Messages.Count - _options.RecentMessageCount)
            .Select(m => $"{m.Role}: {m.Content}")
            .ToList();

        if (olderMessages.Count == 0) return;

        var task = $"Summarize this conversation concisely, preserving key decisions and content direction:\n\n{string.Join("\n\n", olderMessages)}";

        try
        {
            if (!_sidecar.IsConnected)
                await _sidecar.ConnectAsync(ct);

            var sb = new StringBuilder();
            await foreach (var evt in _sidecar.SendTaskAsync(task, "You are a conversation summarizer. Be concise.", null, ct))
            {
                if (evt is ChatEvent chatEvt && chatEvt.Text is not null)
                    sb.Append(chatEvt.Text);
                else if (evt is TaskCompleteEvent)
                    break;
            }
            conversation.ConversationSummary = sb.ToString();
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
