namespace PBA.Domain.Entities;

using PBA.Domain.Enums;

public class PlatformCredential
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Platform Platform { get; init; }
    public string EncryptedAccessToken { get; set; } = string.Empty;
    public string? EncryptedRefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
    public string? Scopes { get; set; }
    public bool IsActive { get; set; }
    public string? EncryptedCookies { get; set; }
    public string? EncryptedIntegrationToken { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
