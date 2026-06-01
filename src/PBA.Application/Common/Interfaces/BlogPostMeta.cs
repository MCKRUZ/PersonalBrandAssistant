namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Metadata for a freshly published blog post, used to weave the post into the
/// static site's index files (blog listing, homepage preview, sitemap).
/// </summary>
public record BlogPostMeta(
    string Slug,
    string Title,
    string Excerpt,
    string Category,
    DateOnly Date,
    string HeroImagePath,
    string Url);
