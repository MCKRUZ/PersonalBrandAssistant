using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class SocialInboxItem : AuditableEntityBase
{
    public PlatformType Platform { get; set; }
    public InboxItemType ItemType { get; set; }
    public string AuthorName { get; set; } = "";
    public string AuthorProfileUrl { get; set; } = "";
    public string Content { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string PlatformItemId { get; set; } = "";
    public bool IsRead { get; set; }
    public string? DraftReply { get; set; }
    public bool ReplySent { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
