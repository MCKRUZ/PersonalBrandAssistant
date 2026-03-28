namespace PersonalBrandAssistant.Application.Common.Models;

public record SubstackPreparedContent(
    string Title,
    string Subtitle,
    string Body,
    string SeoDescription,
    string[] Tags,
    string? SectionName,
    string PreviewText,
    string? CanonicalUrl);

public record SubstackPublishConfirmation(
    Guid ContentId,
    string? SubstackPostUrl,
    bool WasAlreadyPublished);
