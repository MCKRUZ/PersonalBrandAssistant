using System.Text.RegularExpressions;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Infrastructure.Tests.Publishing;

public class BlogIndexUpdaterTests
{
    private readonly BlogIndexUpdater _updater = new();

    private static BlogPostMeta NewPost() => new(
        Slug: "agents-eat-the-org-chart",
        Title: "Agents Eat the Org Chart",
        Excerpt: "Why autonomous agents reshape team structure before they reshape headcount, and what leaders should do about it.",
        Category: "Agentic AI",
        Date: new DateOnly(2026, 6, 1),
        HeroImagePath: "assets/blog-images/agents-eat-the-org-chart.svg",
        Url: "https://matthewkruczek.ai/blog/agents-eat-the-org-chart.html");

    private const string MiniBlogHtml = """
        <body>
            <!-- Featured Post -->
            <section class="featured-post">
                <div class="section-label">Featured Article</div>
                <div class="featured-post-inner">
                    <div class="featured-post-content">
                        <span class="blog-card-category">Enterprise AI</span>
                        <h2><a href="blog/scaling-pilot-to-production.html">From Pilot to Production: The Scaling Playbook</a></h2>
                        <p>
                            88% of organizations use AI. Fewer than 10% have scaled it.
                        </p>
                        <div class="blog-card-meta">
                            <span class="blog-card-date">May 26, 2026</span>
                            <span>·</span>
                            <span>22 min read</span>
                        </div>
                        <a class="blog-card-link" href="blog/scaling-pilot-to-production.html">Read Full Article</a>
                    </div>
                    <div class="featured-post-image">
                        <img loading="lazy" src="assets/blog-images/scaling-pilot-to-production.svg" alt="From Pilot to Production: The Scaling Playbook" style="width: 100%; height: 100%; object-fit: cover;">
                    </div>
                </div>
            </section>

            <!-- Blog List -->
            <section class="blog-list">
                <div class="blog-list-grid" id="blogGrid">

                    <!-- Article: From Pilot to Production -->
                    <article class="blog-card" data-category="enterprise-ai">
                        <div class="blog-card-content">
                            <h3><a href="blog/scaling-pilot-to-production.html">From Pilot to Production: The Scaling Playbook</a></h3>
                        </div>
                    </article>

                    <!-- Article: Scaffolding the Agentic Harness -->
                    <article class="blog-card" data-category="agentic-ai">
                        <div class="blog-card-content">
                            <h3><a href="blog/building-agentic-harness.html">Scaffolding the Agentic Harness</a></h3>
                        </div>
                    </article>

                </div>
            </section>
        </body>
        """;

    private const string MiniIndexHtml = """
        <body>
            <section class="blog" id="blog">
                <div class="blog-grid">
                    <article class="blog-card">
                        <div class="blog-card-content">
                            <div class="blog-card-meta">
                                <span class="blog-card-category">Enterprise AI</span>
                                <span class="blog-card-date">May 2026</span>
                            </div>
                            <h3><a href="blog/scaling-pilot-to-production.html">From Pilot to Production: The Scaling Playbook</a></h3>
                            <p>Old preview excerpt.</p>
                            <a class="blog-card-link" href="blog/scaling-pilot-to-production.html">Read Article</a>
                        </div>
                    </article>
                    <article class="blog-card">
                        <h3><a href="blog/building-agentic-harness.html">Scaffolding the Agentic Harness</a></h3>
                    </article>
                </div>
                <div class="blog-cta">
                    <a class="btn btn-secondary" href="/blog">View All 54 Articles</a>
                </div>
            </section>
        </body>
        """;

    private const string MiniSitemap = """
        <?xml version="1.0" encoding="UTF-8"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
          <url>
            <loc>https://matthewkruczek.ai/</loc>
            <priority>1.0</priority>
            <changefreq>weekly</changefreq>
            <lastmod>2026-05-19</lastmod>
          </url>
          <url>
            <loc>https://matthewkruczek.ai/blog/scaling-pilot-to-production.html</loc>
            <priority>0.6</priority>
            <changefreq>monthly</changefreq>
            <lastmod>2026-05-19</lastmod>
          </url>
        </urlset>
        """;

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    [Fact]
    public void InsertIntoBlogListing_MakesNewPostFeatured()
    {
        var result = _updater.InsertIntoBlogListing(MiniBlogHtml, NewPost());

        var featuredStart = result.IndexOf("<section class=\"featured-post\">", StringComparison.Ordinal);
        var featuredEnd = result.IndexOf("</section>", featuredStart, StringComparison.Ordinal);
        var featuredBlock = result[featuredStart..featuredEnd];

        Assert.Contains("Agents Eat the Org Chart", featuredBlock);
        Assert.Contains("blog/agents-eat-the-org-chart.html", featuredBlock);
        Assert.DoesNotContain("From Pilot to Production", featuredBlock);
    }

    [Fact]
    public void InsertIntoBlogListing_PrependsNewGridCard()
    {
        var result = _updater.InsertIntoBlogListing(MiniBlogHtml, NewPost());

        Assert.Contains(
            "<article class=\"blog-card\" data-category=\"agentic-ai\">",
            result);
        Assert.Contains("blog/agents-eat-the-org-chart.html", result);

        // The new card sits at the top of the grid (immediately after the container).
        var gridStart = result.IndexOf("id=\"blogGrid\">", StringComparison.Ordinal);
        var newCardIndex = result.IndexOf("agents-eat-the-org-chart", gridStart, StringComparison.Ordinal);
        var oldFirstCardIndex = result.IndexOf("Scaffolding the Agentic Harness", gridStart, StringComparison.Ordinal);
        Assert.True(newCardIndex < oldFirstCardIndex, "New card should precede the previously-first card");
    }

    [Fact]
    public void InsertIntoBlogListing_PreservesAllOriginalCards_CountPlusOneFromDemotion()
    {
        // Original grid has 2 cards. After insert: new post + demoted old-featured + 2 originals = 4.
        var before = CountOccurrences(MiniBlogHtml, "<article class=\"blog-card\"");
        Assert.Equal(2, before);

        var result = _updater.InsertIntoBlogListing(MiniBlogHtml, NewPost());
        var after = CountOccurrences(result, "<article class=\"blog-card\"");

        Assert.Equal(4, after);

        // Both original grid cards still present, never truncated.
        Assert.Contains("blog/scaling-pilot-to-production.html", result);
        Assert.Contains("Scaffolding the Agentic Harness", result);
    }

    [Fact]
    public void InsertIntoBlogListing_DemotesOldFeaturedIntoGrid()
    {
        var result = _updater.InsertIntoBlogListing(MiniBlogHtml, NewPost());

        // The previously-featured post now appears as a grid card carrying its category.
        var gridStart = result.IndexOf("id=\"blogGrid\">", StringComparison.Ordinal);
        var grid = result[gridStart..];
        Assert.Contains("From Pilot to Production: The Scaling Playbook", grid);
        Assert.Contains("data-category=\"enterprise-ai\"", grid);
    }

    [Fact]
    public void InsertIntoHomepage_ReplacesPreviewWithNewPost()
    {
        var result = _updater.InsertIntoHomepage(MiniIndexHtml, NewPost());

        var gridStart = result.IndexOf("<div class=\"blog-grid\">", StringComparison.Ordinal);
        var firstArticle = result.IndexOf("<article class=\"blog-card\">", gridStart, StringComparison.Ordinal);
        var firstArticleEnd = result.IndexOf("</article>", firstArticle, StringComparison.Ordinal);
        var firstCard = result[firstArticle..firstArticleEnd];

        Assert.Contains("Agents Eat the Org Chart", firstCard);
        Assert.Contains("blog/agents-eat-the-org-chart.html", firstCard);
        Assert.DoesNotContain("From Pilot to Production", firstCard);

        // The second original card is untouched.
        Assert.Contains("Scaffolding the Agentic Harness", result);
    }

    [Fact]
    public void InsertIntoHomepage_IncrementsArticleCount()
    {
        var result = _updater.InsertIntoHomepage(MiniIndexHtml, NewPost());

        Assert.Contains("View All 55 Articles", result);
        Assert.DoesNotContain("View All 54 Articles", result);
    }

    [Fact]
    public void InsertIntoHomepage_NoCountPresent_LeavesCtaUntouched()
    {
        var html = MiniIndexHtml.Replace("View All 54 Articles", "View All Articles");

        var result = _updater.InsertIntoHomepage(html, NewPost());

        Assert.Contains("View All Articles", result);
        Assert.DoesNotMatch(new Regex(@"View All \d+ Articles"), result);
    }

    [Fact]
    public void InsertIntoSitemap_AddsNewUrlEntry()
    {
        var result = _updater.InsertIntoSitemap(MiniSitemap, NewPost());

        Assert.Contains("<loc>https://matthewkruczek.ai/blog/agents-eat-the-org-chart.html</loc>", result);
        Assert.Contains("</urlset>", result);

        var locCount = CountOccurrences(result, "agents-eat-the-org-chart.html");
        Assert.Equal(1, locCount);
    }

    [Fact]
    public void InsertIntoSitemap_DuplicateSlug_DoesNotAddSecondEntry()
    {
        var existing = NewPost() with
        {
            Slug = "scaling-pilot-to-production",
            Url = "https://matthewkruczek.ai/blog/scaling-pilot-to-production.html",
        };

        var urlsBefore = CountOccurrences(MiniSitemap, "<url>");
        var result = _updater.InsertIntoSitemap(MiniSitemap, existing);
        var urlsAfter = CountOccurrences(result, "<url>");

        Assert.Equal(urlsBefore, urlsAfter);

        var locCount = CountOccurrences(result, "scaling-pilot-to-production.html");
        Assert.Equal(1, locCount);

        // lastmod was refreshed to today rather than left at the old date.
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Assert.Contains($"<lastmod>{today}</lastmod>", result);
    }

    [Fact]
    public void Methods_DoNotThrow_OnRealFileShapes()
    {
        var post = NewPost();

        var blog = _updater.InsertIntoBlogListing(MiniBlogHtml, post);
        var index = _updater.InsertIntoHomepage(MiniIndexHtml, post);
        var sitemap = _updater.InsertIntoSitemap(MiniSitemap, post);

        Assert.NotEmpty(blog);
        Assert.NotEmpty(index);
        Assert.NotEmpty(sitemap);
    }

    [Fact]
    public void InsertIntoBlogListing_MissingFeaturedBlock_StillPrependsCard()
    {
        var htmlNoFeatured = """
            <section class="blog-list">
                <div class="blog-list-grid" id="blogGrid">
                    <article class="blog-card" data-category="enterprise-ai">
                        <h3><a href="blog/existing.html">Existing</a></h3>
                    </article>
                </div>
            </section>
            """;

        var result = _updater.InsertIntoBlogListing(htmlNoFeatured, NewPost());

        Assert.Contains("blog/agents-eat-the-org-chart.html", result);
        Assert.Contains("blog/existing.html", result);
    }
}
