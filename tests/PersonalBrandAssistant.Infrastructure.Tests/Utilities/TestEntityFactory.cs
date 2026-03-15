using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Tests.Utilities;

// Note: CreateContentInState and new factory methods added for Phase 02

public static class TestEntityFactory
{
    public static Content CreateContent(
        ContentType type = ContentType.BlogPost,
        string body = "Test content body",
        string? title = "Test Title",
        PlatformType[]? targetPlatforms = null) =>
        Content.Create(type, body, title, targetPlatforms);

    public static Platform CreatePlatform(
        PlatformType type = PlatformType.TwitterX,
        string displayName = "Test Platform",
        byte[]? accessToken = null,
        byte[]? refreshToken = null) =>
        new()
        {
            Type = type,
            DisplayName = displayName,
            // Dummy bytes; not valid ciphertext. Tests requiring decryption must provide real encrypted tokens.
            EncryptedAccessToken = accessToken ?? [1, 2, 3],
            EncryptedRefreshToken = refreshToken ?? [4, 5, 6],
        };

    public static BrandProfile CreateBrandProfile(
        string name = "Test Brand",
        string personaDescription = "Test persona") =>
        new()
        {
            Name = name,
            PersonaDescription = personaDescription,
        };

    public static User CreateUser(
        string displayName = "Test User",
        string email = "test@example.com",
        string timeZoneId = "America/New_York") =>
        new()
        {
            DisplayName = displayName,
            Email = email,
            TimeZoneId = timeZoneId,
        };

    public static Content CreateArchivedContent(string body = "Archived content")
    {
        var content = CreateContent(body: body);
        content.TransitionTo(ContentStatus.Review);
        content.TransitionTo(ContentStatus.Approved);
        content.TransitionTo(ContentStatus.Scheduled);
        content.TransitionTo(ContentStatus.Publishing);
        content.TransitionTo(ContentStatus.Published);
        content.TransitionTo(ContentStatus.Archived);
        return content;
    }

    public static Content CreateContentInReview(
        AutonomyLevel autonomyLevel = AutonomyLevel.Manual)
    {
        var content = Content.Create(ContentType.BlogPost, "Review content",
            capturedAutonomyLevel: autonomyLevel);
        content.TransitionTo(ContentStatus.Review);
        return content;
    }

    public static Content CreateContentInApproved()
    {
        var content = CreateContent();
        content.TransitionTo(ContentStatus.Review);
        content.TransitionTo(ContentStatus.Approved);
        return content;
    }

    public static Content CreateContentInScheduled(DateTimeOffset scheduledAt)
    {
        var content = CreateContent();
        content.TransitionTo(ContentStatus.Review);
        content.TransitionTo(ContentStatus.Approved);
        content.ScheduledAt = scheduledAt;
        content.TransitionTo(ContentStatus.Scheduled);
        return content;
    }

    public static Content CreateContentInFailed(
        int retryCount = 0,
        DateTimeOffset? nextRetryAt = null)
    {
        var content = CreateContent();
        content.TransitionTo(ContentStatus.Review);
        content.TransitionTo(ContentStatus.Approved);
        content.TransitionTo(ContentStatus.Scheduled);
        content.TransitionTo(ContentStatus.Publishing);
        content.TransitionTo(ContentStatus.Failed);
        content.RetryCount = retryCount;
        content.NextRetryAt = nextRetryAt;
        return content;
    }

    public static Content CreateContentInPublishing(DateTimeOffset publishingStartedAt)
    {
        var content = CreateContent();
        content.TransitionTo(ContentStatus.Review);
        content.TransitionTo(ContentStatus.Approved);
        content.TransitionTo(ContentStatus.Scheduled);
        content.TransitionTo(ContentStatus.Publishing);
        content.PublishingStartedAt = publishingStartedAt;
        return content;
    }

    public static AgentExecution CreateAgentExecution(
        AgentCapabilityType agentType = AgentCapabilityType.Writer,
        ModelTier modelTier = ModelTier.Standard,
        Guid? contentId = null) =>
        AgentExecution.Create(agentType, modelTier, contentId);

    public static AgentExecution CreateRunningAgentExecution(
        AgentCapabilityType agentType = AgentCapabilityType.Writer,
        ModelTier modelTier = ModelTier.Standard,
        Guid? contentId = null)
    {
        var execution = CreateAgentExecution(agentType, modelTier, contentId);
        execution.MarkRunning();
        return execution;
    }

    public static AgentExecution CreateCompletedAgentExecution(
        AgentCapabilityType agentType = AgentCapabilityType.Writer,
        ModelTier modelTier = ModelTier.Standard,
        Guid? contentId = null,
        string? outputSummary = "Test output")
    {
        var execution = CreateRunningAgentExecution(agentType, modelTier, contentId);
        execution.Complete(outputSummary);
        return execution;
    }

    public static ContentPlatformStatus CreateContentPlatformStatus(
        Guid contentId,
        PlatformType platform = PlatformType.TwitterX,
        PlatformPublishStatus status = PlatformPublishStatus.Pending,
        string? idempotencyKey = null) =>
        new()
        {
            ContentId = contentId,
            Platform = platform,
            Status = status,
            IdempotencyKey = idempotencyKey ?? $"{contentId}:{platform}:1",
        };

    public static OAuthState CreateOAuthState(
        PlatformType platform = PlatformType.TwitterX,
        string? state = null,
        DateTimeOffset? expiresAt = null,
        string? codeVerifier = null) =>
        new()
        {
            State = state ?? Guid.NewGuid().ToString("N"),
            Platform = platform,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10),
            EncryptedCodeVerifier = codeVerifier != null ? System.Text.Encoding.UTF8.GetBytes(codeVerifier) : null,
        };

    public static AgentExecutionLog CreateAgentExecutionLog(
        Guid? agentExecutionId = null,
        int stepNumber = 1,
        string stepType = "prompt",
        string? content = "Test prompt content",
        int tokensUsed = 100) =>
        AgentExecutionLog.Create(
            agentExecutionId ?? Guid.NewGuid(),
            stepNumber,
            stepType,
            content,
            tokensUsed);
}
