using MediatR;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IOAuthManager
{
    Task<Result<OAuthAuthorizationUrl>> GenerateAuthUrlAsync(PlatformType platform, CancellationToken ct);
    Task<Result<OAuthTokens>> ExchangeCodeAsync(PlatformType platform, string code, string state, string? codeVerifier, CancellationToken ct);
    Task<Result<OAuthTokens>> RefreshTokenAsync(PlatformType platform, CancellationToken ct);
    Task<Result<Unit>> RevokeTokenAsync(PlatformType platform, CancellationToken ct);
}
