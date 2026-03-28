using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Domain.Entities;

public class Platform : AuditableEntityBase
{
    public PlatformType Type { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public byte[]? EncryptedAccessToken { get; set; }
    public byte[]? EncryptedRefreshToken { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public string[]? GrantedScopes { get; set; }
    public PlatformRateLimitState RateLimitState { get; set; } = new();
    public DateTimeOffset? LastSyncAt { get; set; }
    public PlatformSettings Settings { get; set; } = new();
    public uint Version { get; set; }
}
