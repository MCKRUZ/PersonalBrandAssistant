using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class BlogPublishRequest : AuditableEntityBase
{
    public Guid ContentId { get; set; }
    public string Html { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public BlogPublishStatus Status { get; set; } = BlogPublishStatus.Staged;
    public string? CommitSha { get; set; }
    public string? CommitUrl { get; set; }
    public string? BlogUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public int VerificationAttempts { get; set; }
}
