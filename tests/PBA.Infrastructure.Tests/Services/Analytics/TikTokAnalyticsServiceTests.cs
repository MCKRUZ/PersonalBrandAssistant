using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Services.Analytics;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Analytics;

public class TikTokAnalyticsServiceTests
{
    private readonly Mock<ITikTokDisplayClient> _client = new();
    private readonly Mock<ITokenEncryptor> _encryptor = new();

    public TikTokAnalyticsServiceTests()
    {
        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s);
    }

    private TikTokAnalyticsService CreateService() =>
        new(_client.Object, _encryptor.Object, NullLogger<TikTokAnalyticsService>.Instance);

    private static PlatformCredential Credential() =>
        new() { Platform = Platform.TikTok, EncryptedAccessToken = "token" };

    private void SetupUser(long followers, long following, long likes, long videos) =>
        _client.Setup(c => c.GetUserStatsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, long>
            {
                ["followers"] = followers, ["following"] = following, ["likes"] = likes, ["videos"] = videos
            });

    private static TikTokVideoPage Page(int count, string prefix, string? nextCursor, bool hasMore) =>
        new(Enumerable.Range(0, count).Select(i => new TikTokVideo($"{prefix}{i}", "t", 100, 10, 2, 1)).ToList(),
            nextCursor, hasMore);

    [Fact]
    public async Task PollAsync_MapsUserInfoStats_ToAccountKeys()
    {
        SetupUser(9000, 120, 45000, 300);
        _client.Setup(c => c.GetVideoPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(0, "v", null, false));

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var account = result.Value!.Account.Metrics;
        Assert.Equal(9000, account["followers"]);
        Assert.Equal(120, account["following"]);
        Assert.Equal(45000, account["likes"]);
        Assert.Equal(300, account["videos"]);
    }

    [Fact]
    public async Task PollAsync_PaginatesVideoList_ToReachN()
    {
        SetupUser(1, 1, 1, 1);
        // 20/page: page 1 (has_more=true) then page 2 (has_more=false) accumulate to N=40.
        _client.SetupSequence(c => c.GetVideoPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(20, "a", "cursor1", hasMore: true))
            .ReturnsAsync(Page(20, "b", null, hasMore: false));

        var result = await CreateService().PollAsync(Credential(), 40, CancellationToken.None);

        Assert.Equal(40, result.Value!.RecentVideos.Count);
        _client.Verify(c => c.GetVideoPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task PollAsync_MapsPerVideoCounts()
    {
        SetupUser(1, 1, 1, 1);
        _client.Setup(c => c.GetVideoPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TikTokVideoPage(
                [new TikTokVideo("vid1", "My Clip", 5000, 400, 30, 12)], null, false));

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        var video = result.Value!.RecentVideos.Single();
        Assert.Equal("vid1", video.VideoId);
        Assert.Equal(5000, video.Metrics["views"]);
        Assert.Equal(400, video.Metrics["likes"]);
        Assert.Equal(30, video.Metrics["comments"]);
        Assert.Equal(12, video.Metrics["shares"]);
    }

    [Fact]
    public async Task PollAsync_ApiError_ReturnsResultFail_NotThrow()
    {
        _client.Setup(c => c.GetUserStatsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task PollAsync_EmptyData_ReturnsSuccessWithEmptyMetrics()
    {
        _client.Setup(c => c.GetUserStatsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, long>());
        _client.Setup(c => c.GetVideoPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(0, "v", null, false));

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.RecentVideos);
    }
}
