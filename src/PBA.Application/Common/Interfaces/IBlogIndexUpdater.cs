namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Pure string-in/string-out transformations that insert a newly published post
/// into the static site's index documents. No file IO, no git: callers read the
/// files, pass the contents here, and write the results back. This keeps the
/// weaving logic fully unit-testable.
/// </summary>
public interface IBlogIndexUpdater
{
    /// <summary>
    /// Replaces the featured-post block with <paramref name="post"/>, demotes the
    /// previously featured post into a normal grid card, and prepends a new grid
    /// card for <paramref name="post"/>. All existing cards are preserved.
    /// </summary>
    string InsertIntoBlogListing(string blogHtml, BlogPostMeta post);

    /// <summary>
    /// Replaces the first preview card on the homepage with <paramref name="post"/>
    /// and, if a "View All N Articles" count is present, increments N by one.
    /// </summary>
    string InsertIntoHomepage(string indexHtml, BlogPostMeta post);

    /// <summary>
    /// Adds a sitemap &lt;url&gt; entry for <paramref name="post"/>. If the post's
    /// slug is already present, its lastmod is updated rather than duplicated.
    /// </summary>
    string InsertIntoSitemap(string sitemapXml, BlogPostMeta post);
}
