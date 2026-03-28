using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IBlogChatService
{
    IAsyncEnumerable<string> SendMessageAsync(Guid contentId, string userMessage, CancellationToken ct);
    Task<ChatConversation?> GetConversationAsync(Guid contentId, CancellationToken ct);
    Task<Result<FinalizedDraft>> ExtractFinalDraftAsync(Guid contentId, CancellationToken ct);
}

public record FinalizedDraft(string Title, string Subtitle, string BodyMarkdown, string SeoDescription, string[] Tags);
