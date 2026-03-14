using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class OAuthState : EntityBase
{
    public string State { get; set; } = string.Empty;
    public PlatformType Platform { get; set; }
    public string? CodeVerifier { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
