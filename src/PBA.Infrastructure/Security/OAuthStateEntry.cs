using PBA.Domain.Enums;

namespace PBA.Infrastructure.Security;

// Promoted from a private record inside OAuthService so providers can receive it on ExchangeCodeAsync.
// The coordinator still owns creating, storing, and expiring these entries. Purpose is captured at
// authorize time and read back at callback time so the credential is stamped Publishing vs Analytics.
public sealed record OAuthStateEntry(
    Platform Platform,
    string? CodeVerifier = null,
    CredentialPurpose Purpose = CredentialPurpose.Publishing)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public bool IsExpired => DateTimeOffset.UtcNow - CreatedAt > Ttl;
}
