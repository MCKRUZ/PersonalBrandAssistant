using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.AnalyticsServices;

public class ResiliencePolicyTests
{
    [Fact]
    public async Task SubstackService_RejectsNonSubstackUrl()
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new SubstackOptions { FeedUrl = "https://evil.com/feed" });
        var logger = new Mock<ILogger<SubstackService>>().Object;

        var service = new SubstackService(httpClient, options, logger);
        var result = await service.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task SubstackService_RejectsHttpUrl()
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new SubstackOptions { FeedUrl = "http://matthewkruczek.substack.com/feed" });
        var logger = new Mock<ILogger<SubstackService>>().Object;

        var service = new SubstackService(httpClient, options, logger);
        var result = await service.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task SubstackService_RejectsSubdomainSpoofing()
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new SubstackOptions { FeedUrl = "https://substack.com.evil.com/feed" });
        var logger = new Mock<ILogger<SubstackService>>().Object;

        var service = new SubstackService(httpClient, options, logger);
        var result = await service.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public void SubstackOptions_HasCorrectDefaults()
    {
        var options = new SubstackOptions();

        Assert.Equal("Substack", SubstackOptions.SectionName);
        Assert.Contains("substack.com", options.FeedUrl);
    }

    [Fact]
    public void GoogleAnalyticsService_ConstructsWithResiliencePipeline()
    {
        // Verify the service can be constructed (which builds the resilience pipeline)
        var ga4Mock = new Mock<IGa4Client>();
        var gscMock = new Mock<ISearchConsoleClient>();
        var options = Options.Create(new GoogleAnalyticsOptions());
        var logger = new Mock<ILogger<GoogleAnalyticsService>>().Object;

        var service = new GoogleAnalyticsService(ga4Mock.Object, gscMock.Object, options, logger);

        Assert.NotNull(service);
    }
}
