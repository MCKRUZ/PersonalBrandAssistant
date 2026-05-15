using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Seeding;

public sealed class FeedSeedService(IAppDbContext db) : IFeedSeedService
{
    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.FeedItems.AnyAsync(cancellationToken))
            return 0;

        var items = BuildSeedItems();
        db.FeedItems.AddRange(items);
        await db.SaveChangesAsync(cancellationToken);
        return items.Count;
    }

    private static List<FeedItem> BuildSeedItems()
    {
        var rng = new Random(42);
        var now = DateTimeOffset.UtcNow;
        var items = new List<FeedItem>();

        AddAgentDrafts(items, rng, now);
        AddTrendAlerts(items, rng, now);
        AddIdeaSuggestions(items, rng, now);
        AddAnalyticsHighlights(items, rng, now);
        AddApprovalRequests(items, rng, now);
        AddSystemNotifications(items, rng, now);

        ApplyStateVariation(items, rng);
        return items;
    }

    private static void AddAgentDrafts(List<FeedItem> items, Random rng, DateTimeOffset now)
    {
        var drafts = new (string Title, string Summary, string ContentType, string Platform, int WordCount)[]
        {
            ("AI Trends Weekly Draft", "Weekly roundup of AI developments", "Blog", "Substack", 1200),
            ("LinkedIn Thought Leadership Post", "Enterprise AI adoption insights", "LinkedInPost", "LinkedIn", 350),
            ("Thread: .NET 10 AI Features", "New AI APIs in .NET 10", "Tweet", "Twitter", 280),
            ("Blog: Claude Code Deep Dive", "Hands-on guide to Claude Code CLI", "Blog", "Substack", 1800),
            ("LinkedIn: Personal Brand Tips", "5 tips for tech leaders", "LinkedInPost", "LinkedIn", 400),
            ("Blog: Agent-First Architecture", "Building with AI agents at the core", "Blog", "Substack", 2200),
            ("Tweet: Angular Signals Update", "Quick take on Angular signals", "Tweet", "Twitter", 200),
            ("Blog: Enterprise RAG Patterns", "Retrieval patterns for enterprise", "Blog", "Substack", 1500),
            ("LinkedIn: AI Career Advice", "How to pivot into AI engineering", "LinkedInPost", "LinkedIn", 500),
        };

        foreach (var (title, summary, contentType, platform, wordCount) in drafts)
        {
            items.Add(new FeedItem
            {
                Title = title,
                Summary = summary,
                Type = FeedItemType.AgentDraft,
                Data = JsonSerializer.Serialize(new { contentType, primaryPlatform = platform, wordCount }),
                ActionType = "approve",
                Priority = rng.Next(5) == 0 ? FeedItemPriority.High : FeedItemPriority.Normal,
                CreatedAt = now.AddHours(-rng.Next(1, 168)),
            });
        }
    }

    private static void AddTrendAlerts(List<FeedItem> items, Random rng, DateTimeOffset now)
    {
        var trends = new (string Topic, string Source, int Mentions, string Sentiment)[]
        {
            ("Claude Code", "Twitter", 245, "positive"),
            ("AI Agents", "Reddit", 180, "positive"),
            (".NET 10", "HackerNews", 95, "mixed"),
            ("Angular Signals", "Twitter", 67, "positive"),
            ("Personal Branding", "LinkedIn", 312, "positive"),
            ("MCP Protocol", "GitHub", 54, "positive"),
        };

        foreach (var (topic, source, mentions, sentiment) in trends)
        {
            items.Add(new FeedItem
            {
                Title = $"Trending: {topic}",
                Summary = $"{topic} is trending on {source} with {mentions} mentions",
                Type = FeedItemType.TrendAlert,
                Data = JsonSerializer.Serialize(new { topic, source, mentionCount = mentions, sentiment }),
                ActionType = "view",
                Priority = mentions > 200 ? FeedItemPriority.Urgent : mentions > 100 ? FeedItemPriority.High : FeedItemPriority.Normal,
                CreatedAt = now.AddHours(-rng.Next(1, 72)),
            });
        }
    }

    private static void AddIdeaSuggestions(List<FeedItem> items, Random rng, DateTimeOffset now)
    {
        var ideas = new (string Title, string[] Keywords, double Confidence, string SourceTitle)[]
        {
            ("Content idea: AI in Enterprise", new[] { "AI", "enterprise" }, 0.85, "Enterprise AI Adoption Report"),
            ("Content idea: Developer Productivity", new[] { "productivity", "tools", "AI" }, 0.72, "GitHub Survey Results"),
            ("Content idea: RAG Best Practices", new[] { "RAG", "LLM", "architecture" }, 0.91, "Building RAG Systems"),
            ("Content idea: Personal Brand Growth", new[] { "branding", "growth", "LinkedIn" }, 0.68, "Brand Building 101"),
            ("Content idea: Agentic Workflows", new[] { "agents", "orchestration", "MCP" }, 0.88, "Agent Architecture Patterns"),
            ("Content idea: .NET + AI Integration", new[] { ".NET", "AI", "SDK" }, 0.79, ".NET 10 AI APIs"),
        };

        foreach (var (title, keywords, confidence, sourceTitle) in ideas)
        {
            items.Add(new FeedItem
            {
                Title = title,
                Summary = $"Suggested based on {sourceTitle}",
                Type = FeedItemType.IdeaSuggestion,
                Data = JsonSerializer.Serialize(new { keywords, confidence, sourceIdeaTitle = sourceTitle }),
                ActionType = "create-content",
                Priority = confidence > 0.85 ? FeedItemPriority.High : FeedItemPriority.Normal,
                CreatedAt = now.AddHours(-rng.Next(1, 120)),
            });
        }
    }

    private static void AddAnalyticsHighlights(List<FeedItem> items, Random rng, DateTimeOffset now)
    {
        var highlights = new (string Metric, int Current, int Previous)[]
        {
            ("impressions", 5200, 4100),
            ("followers", 1250, 1180),
            ("engagement_rate", 48, 52),
            ("blog_views", 890, 650),
            ("link_clicks", 320, 280),
            ("profile_views", 430, 510),
        };

        foreach (var (metric, current, previous) in highlights)
        {
            var delta = previous == 0 ? 0 : Math.Round((current - previous) / (double)previous * 100, 1);
            items.Add(new FeedItem
            {
                Title = delta >= 0 ? $"{metric.Replace('_', ' ')} up {delta}%" : $"{metric.Replace('_', ' ')} down {Math.Abs(delta)}%",
                Summary = $"{metric.Replace('_', ' ')}: {previous} -> {current}",
                Type = FeedItemType.AnalyticsHighlight,
                Data = JsonSerializer.Serialize(new { metric, currentValue = current, previousValue = previous, delta }),
                ActionType = "view",
                Priority = Math.Abs(delta) > 20 ? FeedItemPriority.High : FeedItemPriority.Low,
                CreatedAt = now.AddHours(-rng.Next(1, 48)),
            });
        }
    }

    private static void AddApprovalRequests(List<FeedItem> items, Random rng, DateTimeOffset now)
    {
        var requests = new (string Title, string ContentType, string Platform, string RequestedBy)[]
        {
            ("Review: Blog Post on AI Agents", "Blog", "Substack", "system"),
            ("Review: LinkedIn Series Intro", "LinkedInPost", "LinkedIn", "system"),
            ("Review: Twitter Thread Draft", "Tweet", "Twitter", "agent"),
            ("Approve: Weekly Newsletter", "Blog", "Substack", "scheduler"),
        };

        for (var i = 0; i < requests.Length; i++)
        {
            var (title, contentType, platform, requestedBy) = requests[i];
            items.Add(new FeedItem
            {
                Title = title,
                Summary = $"Awaiting your review before publishing to {platform}",
                Type = FeedItemType.ApprovalRequest,
                Data = JsonSerializer.Serialize(new { contentType, primaryPlatform = platform, requestedBy }),
                ActionType = "approve",
                Priority = i == 0 ? FeedItemPriority.Urgent : FeedItemPriority.High,
                CreatedAt = now.AddHours(-rng.Next(1, 24)),
            });
        }
    }

    private static void AddSystemNotifications(List<FeedItem> items, Random rng, DateTimeOffset now)
    {
        var notifications = new (string Title, string Summary, string Category, string Link)[]
        {
            ("API rate limit approaching", "Twitter API at 85% of daily limit", "warning", "/settings/integrations"),
            ("New integration available", "Bluesky connector now supported", "info", "/settings/integrations"),
            ("Scheduled post published", "Blog post went live on Substack", "success", "/content"),
        };

        foreach (var (title, summary, category, link) in notifications)
        {
            items.Add(new FeedItem
            {
                Title = title,
                Summary = summary,
                Type = FeedItemType.SystemNotification,
                Data = JsonSerializer.Serialize(new { category, link }),
                ActionType = "view",
                Priority = FeedItemPriority.Low,
                CreatedAt = now.AddHours(-rng.Next(1, 96)),
            });
        }
    }

    private static void ApplyStateVariation(List<FeedItem> items, Random rng)
    {
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < items.Count; i++)
        {
            var isRead = rng.NextDouble() < 0.4;
            var isActedOn = isRead && rng.NextDouble() < 0.75;
            items[i].IsRead = isRead || isActedOn;
            items[i].IsActedOn = isActedOn;
        }

        items[0].ExpiresAt = now.AddDays(-2);
        items[0].IsRead = true;
        items[1].ExpiresAt = now.AddDays(-1);
        items[2].ExpiresAt = now.AddDays(3);
    }
}
