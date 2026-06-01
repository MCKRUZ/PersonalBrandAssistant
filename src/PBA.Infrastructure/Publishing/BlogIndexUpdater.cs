using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PBA.Application.Common.Interfaces;

namespace PBA.Infrastructure.Publishing;

/// <summary>
/// Pure transformations that weave a published post into the static site's index
/// documents. Insertion points are located via stable structural anchors (a
/// container element, the first card, the urlset close tag) so the logic survives
/// edits elsewhere in the markup rather than relying on absolute offsets.
/// </summary>
public sealed partial class BlogIndexUpdater : IBlogIndexUpdater
{
    private const string FeaturedSectionOpen = "<section class=\"featured-post\">";
    private const string SectionClose = "</section>";
    private const string BlogGridAnchor = "<div class=\"blog-list-grid\" id=\"blogGrid\">";
    private const string HomepageGridAnchor = "<div class=\"blog-grid\">";

    public string InsertIntoBlogListing(string blogHtml, BlogPostMeta post)
    {
        ArgumentNullException.ThrowIfNull(blogHtml);
        ArgumentNullException.ThrowIfNull(post);

        var result = blogHtml;

        // 1. Demote the current featured post into a grid card and swap in the new
        //    featured block. If no featured block exists we skip the swap rather
        //    than throw, so the method stays resilient to markup drift.
        var featuredStart = result.IndexOf(FeaturedSectionOpen, StringComparison.Ordinal);
        BlogPostMeta? demoted = null;

        if (featuredStart >= 0)
        {
            var sectionEnd = result.IndexOf(SectionClose, featuredStart, StringComparison.Ordinal);
            if (sectionEnd >= 0)
            {
                var oldFeaturedEnd = sectionEnd + SectionClose.Length;
                var oldFeatured = result[featuredStart..oldFeaturedEnd];
                demoted = ParseFeaturedBlock(oldFeatured);

                var newFeatured = BuildFeaturedBlock(post);
                result = string.Concat(result[..featuredStart], newFeatured, result[oldFeaturedEnd..]);
            }
        }

        // 2. Prepend the new post's grid card, then the demoted old-featured card,
        //    immediately after the grid container's opening tag. Existing cards are
        //    never touched, so the count is strictly preserved (plus the new ones).
        var gridIndex = result.IndexOf(BlogGridAnchor, StringComparison.Ordinal);
        if (gridIndex >= 0)
        {
            var insertAt = gridIndex + BlogGridAnchor.Length;
            var cards = new StringBuilder();
            cards.Append('\n');
            cards.Append(BuildGridCard(post));
            if (demoted is not null)
                cards.Append(BuildGridCard(demoted));

            result = string.Concat(result[..insertAt], cards.ToString(), result[insertAt..]);
        }

        return result;
    }

    public string InsertIntoHomepage(string indexHtml, BlogPostMeta post)
    {
        ArgumentNullException.ThrowIfNull(indexHtml);
        ArgumentNullException.ThrowIfNull(post);

        var result = indexHtml;

        // Replace the first preview card inside the homepage blog grid. The grid
        // contains <article class="blog-card"> entries; we swap the first one.
        var gridIndex = result.IndexOf(HomepageGridAnchor, StringComparison.Ordinal);
        if (gridIndex >= 0)
        {
            var searchFrom = gridIndex + HomepageGridAnchor.Length;
            var firstArticle = result.IndexOf("<article class=\"blog-card\">", searchFrom, StringComparison.Ordinal);
            if (firstArticle >= 0)
            {
                var articleEnd = result.IndexOf("</article>", firstArticle, StringComparison.Ordinal);
                if (articleEnd >= 0)
                {
                    var oldEnd = articleEnd + "</article>".Length;
                    result = string.Concat(
                        result[..firstArticle],
                        BuildHomepageCard(post),
                        result[oldEnd..]);
                }
            }
        }

        // Bump the "View All N Articles" count if such a count exists. The live
        // file uses a plain "View All Articles" with no number, so leave it
        // untouched when no count is present.
        result = ViewAllCountRegex().Replace(
            result,
            match =>
            {
                var current = int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
                return $"View All {current + 1} Articles";
            },
            1);

        return result;
    }

    public string InsertIntoSitemap(string sitemapXml, BlogPostMeta post)
    {
        ArgumentNullException.ThrowIfNull(sitemapXml);
        ArgumentNullException.ThrowIfNull(post);

        var loc = post.Url;
        var lastmod = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Dedupe by exact loc: if the post URL is already in the sitemap, update its
        // lastmod in place instead of appending a duplicate <url> entry.
        var existing = BuildLocLookupRegex(loc);
        var existingMatch = existing.Match(sitemapXml);
        if (existingMatch.Success)
        {
            var block = existingMatch.Value;
            var updatedBlock = LastmodRegex().IsMatch(block)
                ? LastmodRegex().Replace(block, $"<lastmod>{lastmod}</lastmod>", 1)
                : block.Replace("</url>", $"  <lastmod>{lastmod}</lastmod>\n  </url>", StringComparison.Ordinal);

            return sitemapXml.Replace(block, updatedBlock, StringComparison.Ordinal);
        }

        var entry = BuildSitemapEntry(loc, lastmod);

        var closeIndex = sitemapXml.LastIndexOf("</urlset>", StringComparison.Ordinal);
        if (closeIndex < 0)
            return sitemapXml; // Not a recognizable sitemap; leave untouched.

        return string.Concat(sitemapXml[..closeIndex], entry, sitemapXml[closeIndex..]);
    }

    private static string BuildFeaturedBlock(BlogPostMeta post)
    {
        var category = HtmlEncode(post.Category);
        var title = HtmlEncode(post.Title);
        var excerpt = HtmlEncode(post.Excerpt);
        var date = FormatLongDate(post.Date);
        var href = $"blog/{post.Slug}.html";
        var alt = HtmlEncode(post.Title);

        return $$"""
        <section class="featured-post">
                <div class="section-label">Featured Article</div>
                <div class="featured-post-inner">
                    <div class="featured-post-content">
                        <span class="blog-card-category">{{category}}</span>
                        <h2><a href="{{href}}">{{title}}</a></h2>
                        <p>
                            {{excerpt}}
                        </p>
                        <div class="blog-card-meta">
                            <span class="blog-card-date">{{date}}</span>
                        </div>
                        <a class="blog-card-link" href="{{href}}">
                            Read Full Article
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <line x1="5" y1="12" x2="19" y2="12"></line>
                                <polyline points="12 5 19 12 12 19"></polyline>
                            </svg>
                        </a>
                    </div>
                    <div class="featured-post-image">
                        <img loading="lazy" src="{{post.HeroImagePath}}" alt="{{alt}}" style="width: 100%; height: 100%; object-fit: cover;">
                    </div>
                </div>
            </section>
        """;
    }

    private static string BuildGridCard(BlogPostMeta post)
    {
        var category = HtmlEncode(post.Category);
        var title = HtmlEncode(post.Title);
        var excerpt = HtmlEncode(post.Excerpt);
        var date = FormatLongDate(post.Date);
        var href = $"blog/{post.Slug}.html";
        var alt = HtmlEncode(post.Title);
        var dataCategory = ToDataCategory(post.Category);

        return $$"""

                    <!-- Article: {{title}} -->
                    <article class="blog-card" data-category="{{dataCategory}}">
                        <div class="blog-card-image">
                            <img loading="lazy" src="{{post.HeroImagePath}}" alt="{{alt}}" style="width: 100%; height: 100%; object-fit: cover;">
                        </div>
                        <div class="blog-card-content">
                            <div class="blog-card-meta">
                                <span class="blog-card-category">{{category}}</span>
                                <span class="blog-card-date">{{date}}</span>
                            </div>
                            <h3><a href="{{href}}">{{title}}</a></h3>
                            <p>{{excerpt}}</p>
                            <a class="blog-card-link" href="{{href}}">
                                Read Article
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <line x1="5" y1="12" x2="19" y2="12"></line>
                                    <polyline points="12 5 19 12 12 19"></polyline>
                                </svg>
                            </a>
                        </div>
                    </article>
        """;
    }

    private static string BuildHomepageCard(BlogPostMeta post)
    {
        var category = HtmlEncode(post.Category);
        var title = HtmlEncode(post.Title);
        var excerpt = HtmlEncode(post.Excerpt);
        var date = FormatShortDate(post.Date);
        var href = $"blog/{post.Slug}.html";
        var alt = HtmlEncode(post.Title);

        return $$"""
        <article class="blog-card">
                <div class="blog-card-image">
                        <img src="{{post.HeroImagePath}}" alt="{{alt}}" style="width: 100%; height: 100%; object-fit: cover;">
                    </div>
                    <div class="blog-card-content">
                        <div class="blog-card-meta">
                            <span class="blog-card-category">{{category}}</span>
                            <span class="blog-card-date">{{date}}</span>
                        </div>
                        <h3><a href="{{href}}">{{title}}</a></h3>
                        <p>{{excerpt}}</p>
                        <a class="blog-card-link" href="{{href}}">
                            Read Article
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <line x1="5" y1="12" x2="19" y2="12"></line>
                                <polyline points="12 5 19 12 12 19"></polyline>
                            </svg>
                        </a>
                    </div>
                </article>
        """;
    }

    private static string BuildSitemapEntry(string loc, string lastmod) =>
        $"""
          <url>
            <loc>{HtmlEncode(loc)}</loc>
            <priority>0.6</priority>
            <changefreq>monthly</changefreq>
            <lastmod>{lastmod}</lastmod>
          </url>

        """;

    /// <summary>
    /// Best-effort parse of an existing featured-post block back into a
    /// <see cref="BlogPostMeta"/> so it can be demoted to a grid card. Returns null
    /// when the block cannot be parsed (e.g. unexpected markup), in which case the
    /// caller simply skips the demotion.
    /// </summary>
    private static BlogPostMeta? ParseFeaturedBlock(string featuredHtml)
    {
        var categoryMatch = Regex.Match(
            featuredHtml,
            "<span class=\"blog-card-category\">(?<v>.*?)</span>",
            RegexOptions.Singleline);
        var titleMatch = Regex.Match(
            featuredHtml,
            "<h2><a href=\"(?<href>.*?)\">(?<title>.*?)</a></h2>",
            RegexOptions.Singleline);
        var excerptMatch = Regex.Match(
            featuredHtml,
            "<p>\\s*(?<v>.*?)\\s*</p>",
            RegexOptions.Singleline);
        var dateMatch = Regex.Match(
            featuredHtml,
            "<span class=\"blog-card-date\">(?<v>.*?)</span>",
            RegexOptions.Singleline);
        var imgMatch = Regex.Match(
            featuredHtml,
            "<img[^>]*?src=\"(?<v>.*?)\"",
            RegexOptions.Singleline);

        if (!titleMatch.Success)
            return null;

        var href = titleMatch.Groups["href"].Value;
        var slug = SlugFromHref(href);
        var category = HtmlDecode(categoryMatch.Success ? categoryMatch.Groups["v"].Value : string.Empty);
        var title = HtmlDecode(titleMatch.Groups["title"].Value);
        var excerpt = HtmlDecode(excerptMatch.Success ? excerptMatch.Groups["v"].Value : string.Empty);
        var date = ParseLongDate(dateMatch.Success ? dateMatch.Groups["v"].Value : string.Empty);
        var hero = imgMatch.Success ? imgMatch.Groups["v"].Value : string.Empty;

        return new BlogPostMeta(slug, title, excerpt, category, date, hero, href);
    }

    private static string SlugFromHref(string href)
    {
        // "blog/some-slug.html" -> "some-slug"
        var fileName = href.Split('/').Last();
        return fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".html".Length]
            : fileName;
    }

    private static string ToDataCategory(string category) =>
        category.Trim().ToLowerInvariant().Replace(' ', '-').Replace("&", "and");

    private static string FormatLongDate(DateOnly date) =>
        date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    private static string FormatShortDate(DateOnly date) =>
        date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    private static DateOnly ParseLongDate(string value) =>
        DateOnly.TryParseExact(value.Trim(), "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.UtcNow);

    private static string HtmlEncode(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static string HtmlDecode(string value) => System.Net.WebUtility.HtmlDecode(value);

    private static Regex BuildLocLookupRegex(string loc) =>
        new(
            "<url>\\s*<loc>" + Regex.Escape(HtmlEncode(loc)) + "</loc>.*?</url>",
            RegexOptions.Singleline);

    [GeneratedRegex(@"View All (?<count>\d+) Articles")]
    private static partial Regex ViewAllCountRegex();

    [GeneratedRegex(@"<lastmod>.*?</lastmod>", RegexOptions.Singleline)]
    private static partial Regex LastmodRegex();
}
