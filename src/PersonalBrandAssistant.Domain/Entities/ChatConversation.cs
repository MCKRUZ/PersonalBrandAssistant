using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Entities;

public sealed record ChatMessage(string Role, string Content, DateTimeOffset Timestamp);

public class ChatConversation : AuditableEntityBase
{
    public Guid ContentId { get; set; }
    public List<ChatMessage> Messages { get; set; } = [];
    public string? ConversationSummary { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }
}
