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
}
