using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Services.Analytics;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Analytics;

public class InstagramAnalyticsServiceTests
{
    private readonly Mock<IInstagramGraphClient> _client = new();
    private readonly Mock<ITokenEncryptor> _encryptor = new();

    public InstagramAnalyticsServiceTests()
    {
        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s);
    }

    private InstagramAnalyticsService CreateService() =>
        new(_client.Object, _encryptor.Object, NullLogger<InstagramAnalyticsService>.Instance);

    private static PlatformCredential Credential() =>
        new() { Platform = Platform.Instagram, EncryptedAccessToken = "token" };

    private void SetupAccount(IReadOnlyDictionary<string, long> metrics) =>
        _client.Setup(c => c.GetAccountMetricsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);

    private void SetupMedia(IReadOnlyList<InstagramMediaMetrics> media) =>
        _client.Setup(c => c.GetRecentMediaAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(media);

    [Fact]
    public async Task PollAsync_MapsAccountInsights_ToCanonicalKeys()
    {
        SetupAccount(new Dictionary<string, long>
        {
            ["followers"] = 5000, ["reach"] = 1200, ["views"] = 3400, ["likes"] = 210, ["comments"] = 15
        });
        SetupMedia([]);

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var account = result.Value!.Account.Metrics;
        Assert.Equal(5000, account["followers"]);
        Assert.Equal(1200, account["reach"]);
        Assert.Equal(3400, account["views"]);
    }

    [Fact]
    public async Task PollAsync_DropsUnknownDeprecatedMetric_WithoutFailing()
    {
        // The API dropped "impressions" (deprecated) — the client returns a bag simply lacking it.
        SetupAccount(new Dictionary<string, long> { ["followers"] = 100, ["views"] = 50 });
        SetupMedia([]);

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Account.Metrics.ContainsKey("impressions"));
        Assert.True(result.Value.Account.Metrics.ContainsKey("views"));
    }

    [Fact]
    public async Task PollAsync_MapsPerMediaInsights()
    {
        SetupAccount(new Dictionary<string, long> { ["followers"] = 100 });
        SetupMedia([
            new InstagramMediaMetrics("m1", "caption", new Dictionary<string, long>
            {
                ["reach"] = 300, ["views"] = 900, ["likes"] = 40, ["comments"] = 5, ["saves"] = 8, ["shares"] = 2
            })
        ]);

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        var media = result.Value!.RecentVideos.Single();
        Assert.Equal("m1", media.VideoId);
        Assert.Equal("caption", media.Title);
        Assert.Equal(900, media.Metrics["views"]);
        Assert.Equal(8, media.Metrics["saves"]);
    }

    [Fact]
    public async Task PollAsync_ApiError_ReturnsResultFail_NotThrow()
    {
        _client.Setup(c => c.GetAccountMetricsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task PollAsync_EmptyData_ReturnsSuccessWithEmptyMetrics()
    {
        SetupAccount(new Dictionary<string, long>());
        SetupMedia([]);

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Account.Metrics);
        Assert.Empty(result.Value.RecentVideos);
    }
}
