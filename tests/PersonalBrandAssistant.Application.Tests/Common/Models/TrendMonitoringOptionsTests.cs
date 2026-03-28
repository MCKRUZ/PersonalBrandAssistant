using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Tests.Common.Models;

public class TrendMonitoringOptionsTests
{
    [Fact]
    public void TrendMonitoringOptions_BindsFromConfiguration_AllPropertiesSet()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TrendMonitoring:AggregationIntervalMinutes"] = "60",
                ["TrendMonitoring:TrendRadarApiUrl"] = "http://custom:9000/api",
                ["TrendMonitoring:FreshRssApiUrl"] = "http://rss:8080/api",
                ["TrendMonitoring:RedditSubreddits:0"] = "csharp",
                ["TrendMonitoring:RedditSubreddits:1"] = "aspnetcore",
                ["TrendMonitoring:HackerNewsApiUrl"] = "https://hn.custom/v0",
                ["TrendMonitoring:RelevanceScoreThreshold"] = "0.7",
                ["TrendMonitoring:TitleSimilarityThreshold"] = "0.9",
                ["TrendMonitoring:MaxSuggestionsPerCycle"] = "20",
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<TrendMonitoringOptions>(config.GetSection(TrendMonitoringOptions.SectionName));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TrendMonitoringOptions>>().Value;

        Assert.Equal(60, options.AggregationIntervalMinutes);
        Assert.Equal("http://custom:9000/api", options.TrendRadarApiUrl);
        Assert.Equal("http://rss:8080/api", options.FreshRssApiUrl);
        Assert.Contains("csharp", options.RedditSubreddits);
        Assert.Contains("aspnetcore", options.RedditSubreddits);
        Assert.Equal("https://hn.custom/v0", options.HackerNewsApiUrl);
        Assert.Equal(0.7f, options.RelevanceScoreThreshold);
        Assert.Equal(0.9f, options.TitleSimilarityThreshold);
        Assert.Equal(20, options.MaxSuggestionsPerCycle);
    }

    [Fact]
    public void TrendMonitoringOptions_Defaults_AreReasonable()
    {
        var options = new TrendMonitoringOptions();

        Assert.Equal(30, options.AggregationIntervalMinutes);
        Assert.NotNull(options.RedditSubreddits);
        Assert.NotEmpty(options.RedditSubreddits);
        Assert.Equal(0.6f, options.RelevanceScoreThreshold);
        Assert.Equal(0.85f, options.TitleSimilarityThreshold);
        Assert.Equal(10, options.MaxSuggestionsPerCycle);
    }
}
