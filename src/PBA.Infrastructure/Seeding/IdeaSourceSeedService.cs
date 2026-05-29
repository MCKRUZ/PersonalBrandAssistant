using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Seeding;

public sealed class IdeaSourceSeedService(IAppDbContext db) : IIdeaSourceSeedService
{
    private static readonly (string Name, string FeedUrl, string Category)[] Feeds =
    [
        // AlphaSignal
        ("AlphaSignal", "https://alphasignalai.substack.com/feed", "AI/ML"),

        // Dev.to
        ("DEV Community", "https://dev.to/feed", "General Dev"),

        // TLDR newsletters
        ("TLDR", "https://tldr.tech/api/rss/tech", "Tech"),
        ("TLDR AI", "https://tldr.tech/api/rss/ai", "AI/ML"),
        ("TLDR Web Dev", "https://tldr.tech/api/rss/webdev", "Web Dev"),
        ("TLDR InfoSec", "https://tldr.tech/api/rss/infosec", "Security"),
        ("TLDR Product", "https://tldr.tech/api/rss/product", "Product"),
        ("TLDR DevOps", "https://tldr.tech/api/rss/devops", "DevOps"),
        ("TLDR Founders", "https://tldr.tech/api/rss/founders", "Startups"),
        ("TLDR Design", "https://tldr.tech/api/rss/design", "Design"),
        ("TLDR Marketing", "https://tldr.tech/api/rss/marketing", "Marketing"),
        ("TLDR Crypto", "https://tldr.tech/api/rss/crypto", "Crypto"),
        ("TLDR Fintech", "https://tldr.tech/api/rss/fintech", "Fintech"),
        ("TLDR Data", "https://tldr.tech/api/rss/data", "Data"),
        ("TLDR IT", "https://tldr.tech/api/rss/it", "IT"),
    ];

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingUrls = await db.IdeaSources
            .Where(s => s.FeedUrl != null)
            .Select(s => s.FeedUrl!)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingUrls, StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var (name, feedUrl, category) in Feeds)
        {
            if (existingSet.Contains(feedUrl))
                continue;

            db.IdeaSources.Add(new IdeaSource
            {
                Name = name,
                Type = IdeaSourceType.RSS,
                FeedUrl = feedUrl,
                Category = category,
                PollIntervalMinutes = 60,
                IsEnabled = true,
            });
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);

        return added;
    }
}
