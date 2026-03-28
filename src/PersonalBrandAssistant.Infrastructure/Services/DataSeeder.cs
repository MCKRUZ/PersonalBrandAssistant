using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;

namespace PersonalBrandAssistant.Infrastructure.Services;

public class DataSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DataSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.EnsureCreatedAsync(cancellationToken);

        if (!await context.BrandProfiles.AnyAsync(cancellationToken))
        {
            context.BrandProfiles.Add(new BrandProfile
            {
                Name = "Default Profile",
                PersonaDescription = "Senior software engineer specializing in AI agent development, .NET/Azure architecture, Angular frontends, and cybersecurity. Creates content about Claude/Anthropic ecosystem, MCP integrations, agentic AI patterns, enterprise AI adoption, full-stack .NET development, and modern web engineering.",
                Topics =
                [
                    // AI/ML — core
                    "Claude", "Anthropic", "MCP", "Model Context Protocol", "AI agents", "agentic AI",
                    "tool use", "function calling", "Semantic Kernel", "LangChain",
                    "Microsoft Copilot", "AI orchestration", "LLM", "RAG", "embeddings",
                    "AI safety", "prompt injection", "LLM security",
                    // .NET/C#
                    ".NET", "C#", "ASP.NET", "Blazor", "Entity Framework", "minimal APIs", "MAUI",
                    // Angular/Frontend
                    "Angular", "TypeScript", "NgRx", "RxJS", "frontend architecture",
                    // Azure/Cloud
                    "Azure", "Azure DevOps", "Azure Functions", "cloud architecture", "Kubernetes",
                    // Security
                    "cybersecurity", "OWASP", "red team", "penetration testing", "zero trust",
                    // Docker/Infra
                    "Docker", "containers", "CI/CD", "DevOps", "infrastructure as code",
                ],
                IsActive = true,
            });
            _logger.LogInformation("Seeded default BrandProfile");
        }

        if (!await context.Platforms.AnyAsync(cancellationToken))
        {
            var platforms = Enum.GetValues<PlatformType>().Select(type => new Platform
            {
                Type = type,
                DisplayName = type.ToString(),
                IsConnected = false,
            });
            context.Platforms.AddRange(platforms);
            _logger.LogInformation("Seeded {Count} Platform records", Enum.GetValues<PlatformType>().Length);
        }

        if (!await context.Users.AnyAsync(cancellationToken))
        {
            context.Users.Add(new User
            {
                Email = _configuration["DefaultUser:Email"] ?? "user@example.com",
                DisplayName = "Default User",
                TimeZoneId = _configuration["DefaultUser:TimeZoneId"] ?? "America/New_York",
            });
            _logger.LogInformation("Seeded default User");
        }

        if (!await context.AutonomyConfigurations.AnyAsync(cancellationToken))
        {
            context.AutonomyConfigurations.Add(AutonomyConfiguration.CreateDefault());
            _logger.LogInformation("Seeded default AutonomyConfiguration");
        }

        if (!await context.TrendSettings.AnyAsync(cancellationToken))
        {
            context.TrendSettings.Add(TrendSettings.CreateDefault());
            _logger.LogInformation("Seeded default TrendSettings");
        }

        {
            var defaultFeeds = GetDefaultRssFeeds();
            var existingNames = await context.TrendSources
                .Where(s => s.Type == TrendSourceType.RssFeed)
                .Select(s => s.Name)
                .ToListAsync(cancellationToken);
            var existingSet = existingNames.ToHashSet();
            var newFeeds = defaultFeeds.Where(f => !existingSet.Contains(f.Name)).ToList();
            if (newFeeds.Count > 0)
            {
                context.TrendSources.AddRange(newFeeds);
                _logger.LogInformation("Seeded {Count} new RSS feed sources", newFeeds.Count);
            }
        }

        if (!await context.InterestKeywords.AnyAsync(cancellationToken))
        {
            var keywords = GetDefaultInterestKeywords();
            context.InterestKeywords.AddRange(keywords);
            _logger.LogInformation("Seeded {Count} interest keywords", keywords.Count);
        }

        await SeedEngagementTaskIfMissing(context, PlatformType.Reddit, new EngagementTask
        {
            Platform = PlatformType.Reddit,
            TaskType = EngagementTaskType.Comment,
            TargetCriteria = """{"subreddits":["dotnet","csharp","angular","MachineLearning"],"keywords":["Claude","AI agents","MCP","LLM"],"sort":"hot"}""",
            CronExpression = "0 */4 * * *",
            IsEnabled = true,
            AutoRespond = false,
            MaxActionsPerExecution = 3,
            SchedulingMode = SchedulingMode.HumanLike,
        }, cancellationToken);

        await SeedEngagementTaskIfMissing(context, PlatformType.TwitterX, new EngagementTask
        {
            Platform = PlatformType.TwitterX,
            TaskType = EngagementTaskType.Comment,
            TargetCriteria = """{"keywords":["AI","Claude","LLM","AI agents","MCP"],"hashtags":["aiagents","buildwithai","dotnet"]}""",
            CronExpression = "0 */6 * * *",
            IsEnabled = true,
            AutoRespond = false,
            MaxActionsPerExecution = 2,
            SchedulingMode = SchedulingMode.HumanLike,
        }, cancellationToken);

        await SeedEngagementTaskIfMissing(context, PlatformType.LinkedIn, new EngagementTask
        {
            Platform = PlatformType.LinkedIn,
            TaskType = EngagementTaskType.Comment,
            TargetCriteria = """{"keywords":["AI engineering","LLM agents","developer tools","Claude","agentic AI",".NET AI"]}""",
            CronExpression = "0 9,14 * * 1-5",
            IsEnabled = true,
            AutoRespond = false,
            MaxActionsPerExecution = 2,
            SchedulingMode = SchedulingMode.HumanLike,
        }, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        await TryAutoConnectRedditAsync(scope.ServiceProvider, context, cancellationToken);
    }

    private async Task TryAutoConnectRedditAsync(
        IServiceProvider services, ApplicationDbContext context, CancellationToken ct)
    {
        var clientId     = _configuration["PlatformIntegrations:Reddit:ClientId"];
        var clientSecret = _configuration["PlatformIntegrations:Reddit:ClientSecret"];
        var username     = _configuration["PlatformIntegrations:Reddit:Username"];
        var password     = _configuration["PlatformIntegrations:Reddit:Password"];
        var userAgent    = _configuration["PlatformIntegrations:Reddit:UserAgent"] ?? "pba/1.0";

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) ||
            string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return;

        var platform = await context.Platforms.FirstOrDefaultAsync(p => p.Type == PlatformType.Reddit, ct);
        if (platform is null) return;

        if (platform.IsConnected && platform.EncryptedAccessToken is not null)
        {
            // Verify the token is still decryptable (ephemeral keys change on every container restart)
            try
            {
                var enc = services.GetRequiredService<IEncryptionService>();
                enc.Decrypt(platform.EncryptedAccessToken);
                _logger.LogDebug("Reddit already connected with valid token, skipping auto-connect");
                return;
            }
            catch
            {
                _logger.LogInformation("Reddit token unreadable (key rotation), re-authenticating");
            }
        }

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            var response = await http.PostAsync(
                "https://www.reddit.com/api/v1/access_token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["username"]   = username,
                    ["password"]   = password,
                    ["scope"]      = "identity read submit privatemessages history",
                }), ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Reddit password grant failed: {Status}", response.StatusCode);
                return;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!json.TryGetProperty("access_token", out var tokenProp))
            {
                _logger.LogWarning("Reddit password grant response missing access_token");
                return;
            }

            var accessToken = tokenProp.GetString()!;
            var encryption  = services.GetRequiredService<IEncryptionService>();

            platform.EncryptedAccessToken = encryption.Encrypt(accessToken);
            platform.IsConnected = true;
            platform.DisplayName = $"Reddit (u/{username})";

            await context.SaveChangesAsync(ct);
            _logger.LogInformation("Reddit auto-connected via password grant for u/{Username}", username);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reddit auto-connect failed");
        }
    }

    private static List<TrendSource> GetDefaultRssFeeds() =>
    [
        // .NET/C#
        Feed(".NET Blog", "https://devblogs.microsoft.com/dotnet/feed/", ".NET/C#"),
        Feed("Visual Studio Blog", "https://devblogs.microsoft.com/visualstudio/feed/", ".NET/C#"),
        Feed("TypeScript Blog", "https://devblogs.microsoft.com/typescript/feed/", ".NET/C#"),
        Feed("PowerShell Blog", "https://devblogs.microsoft.com/powershell/feed/", ".NET/C#"),
        Feed("Windows Command Line", "https://devblogs.microsoft.com/commandline/feed/", ".NET/C#"),
        Feed("Microsoft 365 Developer", "https://devblogs.microsoft.com/microsoft365dev/feed/", ".NET/C#"),
        Feed("Andrew Lock", "https://andrewlock.net/rss.xml", ".NET/C#"),
        Feed("Scott Hanselman", "https://feeds.hanselman.com/ScottHanselman", ".NET/C#"),
        Feed("Nick Chapsas (YouTube)", "https://www.youtube.com/feeds/videos.xml?channel_id=UCrkPsvLGln62OMZRO6K-llg", ".NET/C#"),
        Feed("Steven Giesel", "https://steven-giesel.com/feed.rss", ".NET/C#"),
        Feed("Khalid Abuhakmeh", "https://khalidabuhakmeh.com/feed.xml", ".NET/C#"),
        Feed("Jimmy Bogard", "https://www.jimmybogard.com/rss/", ".NET/C#"),

        // AI/ML — Labs & Companies
        Feed("Anthropic Blog", "https://www.anthropic.com/feed.xml", "AI/ML"),
        Feed("OpenAI Blog", "https://openai.com/blog/rss.xml", "AI/ML"),
        Feed("Google DeepMind Blog", "https://deepmind.google/blog/rss.xml", "AI/ML"),
        Feed("Google Research Blog", "https://research.google/blog/rss", "AI/ML"),
        Feed("Meta AI Blog", "https://ai.meta.com/blog/rss/", "AI/ML"),
        Feed("Apple ML Research", "https://machinelearning.apple.com/rss.xml", "AI/ML"),
        Feed("NVIDIA Developer Blog", "https://developer.nvidia.com/blog/feed/", "AI/ML"),
        Feed("NVIDIA Blog", "https://blogs.nvidia.com/feed/", "AI/ML"),
        Feed("AWS Machine Learning Blog", "https://aws.amazon.com/blogs/machine-learning/feed/", "AI/ML"),
        Feed("Cohere Blog", "https://txt.cohere.ai/rss/", "AI/ML"),
        Feed("HuggingFace Blog", "https://huggingface.co/blog/feed.xml", "AI/ML"),
        Feed("LangChain Blog", "https://blog.langchain.dev/rss/", "AI/ML"),
        Feed("Microsoft AI Blog", "https://blogs.microsoft.com/ai/feed/", "AI/ML"),
        Feed("Semantic Kernel Blog", "https://devblogs.microsoft.com/semantic-kernel/feed/", "AI/ML"),

        // AI/ML — News & Publications
        Feed("MIT Technology Review", "https://www.technologyreview.com/feed/", "AI/ML"),
        Feed("VentureBeat AI", "https://venturebeat.com/category/ai/feed/", "AI/ML"),
        Feed("THE DECODER", "https://the-decoder.com/feed/", "AI/ML"),
        Feed("AI Business", "https://aibusiness.com/rss.xml", "AI/ML"),
        Feed("InfoQ AI/ML", "https://feed.infoq.com/ai-ml-data-eng/", "AI/ML"),
        Feed("IEEE Spectrum AI", "https://spectrum.ieee.org/feeds/topic/artificial-intelligence.rss", "AI/ML"),
        Feed("Wired AI", "https://www.wired.com/feed/tag/ai/latest/rss", "AI/ML"),
        Feed("The Verge AI", "https://www.theverge.com/rss/ai-artificial-intelligence/index.xml", "AI/ML"),
        Feed("Ars Technica AI", "https://arstechnica.com/ai/feed/", "AI/ML"),

        // AI/ML — Research & Individual Blogs
        Feed("Simon Willison", "https://simonwillison.net/atom/everything/", "AI/ML"),
        Feed("Lilian Weng", "https://lilianweng.github.io/index.xml", "AI/ML"),
        Feed("The Gradient", "https://thegradient.pub/rss/", "AI/ML"),
        Feed("BAIR Blog", "https://bair.berkeley.edu/blog/feed.xml", "AI/ML"),
        Feed("fast.ai Blog", "http://www.fast.ai/atom.xml", "AI/ML"),
        Feed("Sebastian Raschka", "https://magazine.sebastianraschka.com/feed", "AI/ML"),
        Feed("Chip Huyen", "https://huyenchip.com/feed.xml", "AI/ML"),
        Feed("KDnuggets", "https://www.kdnuggets.com/feed", "AI/ML"),
        Feed("Gradient Flow", "https://gradientflow.com/feed/", "AI/ML"),
        Feed("Machine Learning Mastery", "https://machinelearningmastery.com/blog/feed", "AI/ML"),
        Feed("Towards AI", "https://pub.towardsai.net/feed", "AI/ML"),

        // AI/ML — Newsletters
        Feed("Import AI (Jack Clark)", "https://importai.substack.com/feed", "AI/ML"),
        Feed("Last Week in AI", "https://lastweekin.ai/feed", "AI/ML"),
        Feed("The Rundown AI", "https://rss.beehiiv.com/feeds/2R3C6Bt5wj.xml", "AI/ML"),
        Feed("Latent Space", "https://www.latent.space/feed", "AI/ML"),

        // TLDR Newsletters (via bullrich.dev/tldr-rss third-party converter)
        Feed("TLDR Tech", "https://bullrich.dev/tldr-rss/tech.rss", "General Tech"),
        Feed("TLDR AI", "https://bullrich.dev/tldr-rss/ai.rss", "AI/ML"),
        Feed("TLDR DevOps", "https://bullrich.dev/tldr-rss/devops.rss", "Docker/Infra"),
        Feed("TLDR Data", "https://bullrich.dev/tldr-rss/data.rss", "AI/ML"),
        FeedDisabled("TLDR All (KTN Backup)", "https://kill-the-newsletter.com/feeds/rlzmozqqblwaphdn9wb0.xml", "General Tech"),

        // AI/ML — YouTube
        Feed("Two Minute Papers (YouTube)", "https://www.youtube.com/feeds/videos.xml?channel_id=UCbfYPyITQ-7l4upoX8nvctg", "AI/ML"),
        Feed("Yannic Kilcher (YouTube)", "https://www.youtube.com/feeds/videos.xml?channel_id=UCZHmQk67mSJgfCCTn7xBfew", "AI/ML"),

        // Angular/Frontend
        Feed("Angular Blog", "https://blog.angular.dev/feed", "Angular/Frontend"),
        Feed("Netanel Basal", "https://netbasal.com/feed", "Angular/Frontend"),
        Feed("This is Angular", "https://dev.to/feed/tag/angular", "Angular/Frontend"),
        Feed("Kevin Kreuzer", "https://kevinkreuzer.medium.com/feed", "Angular/Frontend"),

        // Medium Publications
        Feed("Towards Data Science", "https://towardsdatascience.com/feed", "AI/ML"),
        Feed("Better Programming", "https://betterprogramming.pub/feed", "General Tech"),
        Feed("JavaScript in Plain English", "https://javascript.plainenglish.io/feed", "Angular/Frontend"),
        Feed("Level Up Coding", "https://levelup.gitconnected.com/feed", "General Tech"),
        Feed("Medium Engineering", "https://medium.engineering/feed", "General Tech"),

        // Azure/Cloud
        Feed("Azure Blog", "https://azure.microsoft.com/en-us/blog/feed/", "Azure/Cloud"),
        Feed("Azure Updates", "https://azurecomcdn.azureedge.net/en-us/updates/feed/", "Azure/Cloud"),
        Feed("Azure DevOps Blog", "https://devblogs.microsoft.com/devops/feed/", "Azure/Cloud"),
        Feed("Azure SDK Blog", "https://devblogs.microsoft.com/azure-sdk/feed/", "Azure/Cloud"),
        Feed("Pulumi Blog", "https://www.pulumi.com/blog/rss.xml", "Azure/Cloud"),

        // Security
        Feed("Krebs on Security", "https://krebsonsecurity.com/feed/", "Security"),
        Feed("Schneier on Security", "https://www.schneier.com/feed/atom/", "Security"),
        Feed("The Hacker News (Security)", "https://feeds.feedburner.com/TheHackersNews", "Security"),
        Feed("NIST Cybersecurity", "https://www.nist.gov/blogs/cybersecurity-insights/rss.xml", "Security"),
        Feed("Microsoft Security Blog", "https://www.microsoft.com/en-us/security/blog/feed/", "Security"),

        // Docker/Infra
        Feed("Docker Blog", "https://www.docker.com/blog/feed/", "Docker/Infra"),
        Feed("Kubernetes Blog", "https://kubernetes.io/feed.xml", "Docker/Infra"),
        Feed("Portainer Blog", "https://www.portainer.io/blog/rss.xml", "Docker/Infra"),

        // General Tech
        Feed("Hacker News Best", "https://hnrss.org/best", "General Tech"),
        Feed("Ars Technica", "https://feeds.arstechnica.com/arstechnica/index", "General Tech"),
        Feed("The Verge", "https://www.theverge.com/rss/index.xml", "General Tech"),
        Feed("TechCrunch", "https://techcrunch.com/feed/", "General Tech"),
    ];

    private static TrendSource Feed(string name, string feedUrl, string category) => new()
    {
        Name = name,
        Type = TrendSourceType.RssFeed,
        FeedUrl = feedUrl,
        Category = category,
        PollIntervalMinutes = 60,
        IsEnabled = true,
    };

    private static TrendSource FeedDisabled(string name, string feedUrl, string category) => new()
    {
        Name = name,
        Type = TrendSourceType.RssFeed,
        FeedUrl = feedUrl,
        Category = category,
        PollIntervalMinutes = 60,
        IsEnabled = false,
    };

    private static List<InterestKeyword> GetDefaultInterestKeywords() =>
    [
        // Tier 1 — Always flag (weight 1.0)
        Keyword("Claude", 1.0),
        Keyword("Anthropic", 1.0),
        Keyword("MCP", 1.0),
        Keyword("Model Context Protocol", 1.0),
        Keyword("AI agents", 1.0),
        Keyword("agentic AI", 1.0),
        Keyword("tool use", 1.0),
        Keyword("function calling", 1.0),

        // Tier 2 — High (weight 0.8)
        Keyword("Azure AI", 0.8),
        Keyword(".NET AI", 0.8),
        Keyword("Semantic Kernel", 0.8),
        Keyword("Blazor AI", 0.8),
        Keyword("C# ML", 0.8),
        Keyword("Microsoft Copilot", 0.8),
        Keyword("AI orchestration", 0.8),

        // Tier 3 — Medium (weight 0.6)
        Keyword("cybersecurity AI", 0.6),
        Keyword("AI red team", 0.6),
        Keyword("prompt injection", 0.6),
        Keyword("LLM security", 0.6),
        Keyword("AI safety", 0.6),
        Keyword("OWASP LLM", 0.6),

        // Tier 4 — Aware (weight 0.4)
        Keyword("voice AI", 0.4),
        Keyword("image generation", 0.4),
        Keyword("video AI", 0.4),
        Keyword("Sora", 0.4),
        Keyword("fintech AI", 0.4),
        Keyword("AI trading", 0.4),
    ];

    private static InterestKeyword Keyword(string keyword, double weight) => new()
    {
        Keyword = keyword,
        Weight = weight,
    };

    private async Task SeedEngagementTaskIfMissing(
        ApplicationDbContext context, PlatformType platform, EngagementTask task,
        CancellationToken ct)
    {
        var exists = await context.EngagementTasks
            .AnyAsync(t => t.Platform == platform && t.TaskType == task.TaskType, ct);
        if (exists) return;

        context.EngagementTasks.Add(task);
        _logger.LogInformation("Seeded default {Platform} engagement task (disabled)", platform);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
