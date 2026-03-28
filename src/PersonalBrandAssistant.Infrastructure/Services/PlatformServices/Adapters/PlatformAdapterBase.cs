using System.Net;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;

public abstract class PlatformAdapterBase : ISocialPlatform
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IEncryptionService _encryption;
    private readonly IRateLimiter _rateLimiter;
    private readonly IOAuthManager _oauthManager;
    protected IMediaStorage MediaStorage { get; }
    protected ILogger Logger { get; }

    protected PlatformAdapterBase(
        IApplicationDbContext dbContext,
        IEncryptionService encryption,
        IRateLimiter rateLimiter,
        IOAuthManager oauthManager,
        IMediaStorage mediaStorage,
        ILogger logger)
    {
        _dbContext = dbContext;
        _encryption = encryption;
        _rateLimiter = rateLimiter;
        _oauthManager = oauthManager;
        MediaStorage = mediaStorage;
        Logger = logger;
    }

    public abstract PlatformType Type { get; }

    public async Task<Result<PublishResult>> PublishAsync(PlatformContent content, CancellationToken ct)
    {
        return await ExecuteWithTokenAsync("publish", ct,
            (token, c) => ExecutePublishAsync(token, content, c));
    }

    public async Task<Result<Unit>> DeletePostAsync(string platformPostId, CancellationToken ct)
    {
        return await ExecuteWithTokenAsync<Unit>("delete", ct,
            (token, c) => ExecuteDeletePostAsync(token, platformPostId, c));
    }

    public async Task<Result<EngagementStats>> GetEngagementAsync(string platformPostId, CancellationToken ct)
    {
        return await ExecuteWithTokenAsync("engagement", ct,
            (token, c) => ExecuteGetEngagementAsync(token, platformPostId, c));
    }

    public async Task<Result<PlatformProfile>> GetProfileAsync(CancellationToken ct)
    {
        return await ExecuteWithTokenAsync("profile", ct,
            (token, c) => ExecuteGetProfileAsync(token, c));
    }

    public abstract Task<Result<ContentValidation>> ValidateContentAsync(
        PlatformContent content, CancellationToken ct);

    public virtual Task<Result<PlatformPublishStatusCheck>> CheckPublishStatusAsync(
        string platformPostId, CancellationToken ct)
    {
        return Task.FromResult(Result.Success(
            new PlatformPublishStatusCheck(PlatformPublishStatus.Published, null, null)));
    }

    protected abstract Task<Result<PublishResult>> ExecutePublishAsync(
        string accessToken, PlatformContent content, CancellationToken ct);

    protected abstract Task<Result<Unit>> ExecuteDeletePostAsync(
        string accessToken, string platformPostId, CancellationToken ct);

    protected abstract Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
        string accessToken, string platformPostId, CancellationToken ct);

    protected abstract Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
        string accessToken, CancellationToken ct);

    protected abstract (int? Remaining, DateTimeOffset? ResetAt) ParseRateLimitHeaders(
        HttpResponseMessage response);

    private async Task<Result<T>> ExecuteWithTokenAsync<T>(
        string endpoint, CancellationToken ct,
        Func<string, CancellationToken, Task<Result<T>>> execute)
    {
        var tokenResult = await LoadAccessTokenAsync(ct);
        if (!tokenResult.IsSuccess)
        {
            return Result.Failure<T>(tokenResult.ErrorCode, tokenResult.Errors.ToArray());
        }

        var rateLimitResult = await _rateLimiter.CanMakeRequestAsync(Type, endpoint, ct);
        if (rateLimitResult.IsSuccess && !rateLimitResult.Value!.Allowed)
        {
            Logger.LogWarning("Rate limited for {Platform}/{Endpoint}: {Reason}",
                Type, endpoint, rateLimitResult.Value.Reason);
            return Result.Failure<T>(ErrorCode.ValidationFailed,
                $"Rate limited: {rateLimitResult.Value.Reason}");
        }

        var result = await execute(tokenResult.Value!, ct);

        if (!result.IsSuccess && result.ErrorCode == ErrorCode.Unauthorized)
        {
            Logger.LogInformation("401 received for {Platform}, attempting token refresh", Type);
            var refreshResult = await _oauthManager.RefreshTokenAsync(Type, ct);
            if (refreshResult.IsSuccess)
            {
                var newToken = await LoadAccessTokenAsync(ct);
                if (newToken.IsSuccess)
                {
                    result = await execute(newToken.Value!, ct);
                }
            }
            else
            {
                Logger.LogWarning("Token refresh failed for {Platform}", Type);
                return Result.Failure<T>(ErrorCode.Unauthorized, "Token refresh failed");
            }
        }

        return result;
    }

    protected Task<Result<string>> GetAccessTokenAsync(CancellationToken ct) =>
        LoadAccessTokenAsync(ct);

    private async Task<Result<string>> LoadAccessTokenAsync(CancellationToken ct)
    {
        var platform = await _dbContext.Platforms
            .FirstOrDefaultAsync(p => p.Type == Type, ct);

        if (platform is null)
        {
            return Result.NotFound<string>($"Platform '{Type}' not configured");
        }

        if (!platform.IsConnected || platform.EncryptedAccessToken is null)
        {
            return Result.Failure<string>(ErrorCode.Unauthorized, "Platform not connected");
        }

        var token = _encryption.Decrypt(platform.EncryptedAccessToken);
        return Result.Success(token);
    }

    protected async Task RecordRateLimitAsync(
        HttpResponseMessage response, string endpoint, CancellationToken ct)
    {
        var (remaining, resetAt) = ParseRateLimitHeaders(response);
        if (remaining.HasValue)
        {
            await _rateLimiter.RecordRequestAsync(
                Type, endpoint, remaining.Value, resetAt, ct);
        }
    }

    protected static Result<T> ValidatePostIdFormat<T>(string platformPostId, Regex pattern, string platformName)
    {
        if (!pattern.IsMatch(platformPostId))
            return Result.Failure<T>(ErrorCode.ValidationFailed, $"Invalid {platformName} post ID format");
        return default!; // null indicates valid — callers check IsSuccess
    }

    protected static Result<T> HandleHttpError<T>(HttpResponseMessage response, string context)
    {
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => Result.Failure<T>(ErrorCode.Unauthorized, $"{context}: unauthorized"),
            HttpStatusCode.Forbidden => Result.Failure<T>(ErrorCode.ValidationFailed, $"{context}: forbidden"),
            HttpStatusCode.TooManyRequests => Result.Failure<T>(ErrorCode.InternalError, $"{context}: rate limited"),
            HttpStatusCode.NotFound => Result.NotFound<T>($"{context}: not found"),
            _ => Result.Failure<T>(ErrorCode.InternalError, $"{context}: failed with status {(int)response.StatusCode}"),
        };
    }
}
