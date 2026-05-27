namespace PBA.Application.Common.Models;

public record PreprocessedContent(
    string Title,
    string Body,
    string? CanonicalUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ImageReference> Images,
    string? ContentType = null,
    DateTimeOffset? CreatedAt = null
);
