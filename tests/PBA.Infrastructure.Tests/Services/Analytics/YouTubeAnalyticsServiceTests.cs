using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Services.Analytics;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Analytics;

public class YouTubeAnalyticsServiceTests
{
    private readonly Mock<IYouTubeApiClient> _client = new();
    private readonly Mock<ITokenEncryptor> _encryptor = new();

    public YouTubeAnalyticsServiceTests()
    {
        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s);
    }

    private YouTubeAnalyticsService CreateService() =>
        new(_client.Object, _encryptor.Object, NullLogger<YouTubeAnalyticsService>.Instance);

    private static PlatformCredential Credential() =>
        new() { Platform = Platform.YouTube, EncryptedAccessToken = "token" };

    private void SetupChannel(long subs, long views, long videos, string uploadsPlaylistId = "UP1") =>
        _client.Setup(c => c.GetChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YouTubeChannelStats(subs, views, videos, uploadsPlaylistId));

    private void SetupUploads(IReadOnlyList<string> ids, string? nextPageToken = null) =>
        _client.Setup(c => c.GetUploadsPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YouTubePlaylistPage(ids, nextPageToken));

    [Fact]
    public async Task PollAsync_MapsChannelStatistics_ToCumulativeAccountKeys()
    {
        SetupChannel(1000, 50000, 42);
        SetupUploads([]);
        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var account = result.Value!.Account.Metrics;
        Assert.Equal(1000, account["subscribers"]);
        Assert.Equal(50000, account["views"]);
        Assert.Equal(42, account["videos"]);
        Assert.Equal(3, account.Count);   // integer-only bag with exactly the canonical keys
    }

    [Fact]
    public async Task PollAsync_DiscoversRecentVideos_ViaUploadsPlaylist_NotSearch()
    {
        SetupChannel(1, 1, 2);
        SetupUploads(["v1", "v2"]);
        _client.Setup(c => c.GetVideosBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> ids, string _, CancellationToken _) =>
                ids.Select(id => new YouTubeVideoStat(id, "t", 10, 2, 1)).ToList());

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // There is NO search seam on IYouTubeApiClient — discovery structurally uses the uploads playlist.
        _client.Verify(c => c.GetUploadsPageAsync("UP1", null, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.Equal(2, result.Value!.RecentVideos.Count);
    }

    [Fact]
    public async Task PollAsync_BatchesVideosList_MaxFiftyIds()
    {
        SetupChannel(1, 1, 120);
        var ids = Enumerable.Range(0, 120).Select(i => $"v{i}").ToList();
        SetupUploads(ids);

        var batchSizes = new List<int>();
        _client.Setup(c => c.GetVideosBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> batch, string _, CancellationToken _) =>
            {
                batchSizes.Add(batch.Count);
                return batch.Select(id => new YouTubeVideoStat(id, "t", 1, 1, 1)).ToList();
            });

        await CreateService().PollAsync(Credential(), 120, CancellationToken.None);

        Assert.All(batchSizes, size => Assert.True(size <= 50, $"batch of {size} exceeds 50"));
        Assert.Equal(120, batchSizes.Sum());
    }

    [Fact]
    public async Task PollAsync_PagesUploadsPlaylist_AcrossPageTokens_ToReachN()
    {
        SetupChannel(1, 1, 15);
        // Page 1: 10 ids + a next-page token; page 2: 5 more + null. Proves pageToken advancement.
        _client.SetupSequence(c => c.GetUploadsPageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YouTubePlaylistPage(Enumerable.Range(0, 10).Select(i => $"v{i}").ToList(), "PAGE2"))
            .ReturnsAsync(new YouTubePlaylistPage(Enumerable.Range(10, 5).Select(i => $"v{i}").ToList(), null));
        _client.Setup(c => c.GetVideosBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> batch, string _, CancellationToken _) =>
                batch.Select(id => new YouTubeVideoStat(id, "t", 1, 1, 1)).ToList());

        var result = await CreateService().PollAsync(Credential(), 12, CancellationToken.None);

        Assert.Equal(12, result.Value!.RecentVideos.Count);
        // The second page must be fetched with the token the first page returned.
        _client.Verify(c => c.GetUploadsPageAsync("UP1", "PAGE2", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PollAsync_CapsRecentVideos_AtN()
    {
        SetupChannel(1, 1, 100);
        SetupUploads(Enumerable.Range(0, 100).Select(i => $"v{i}").ToList());
        _client.Setup(c => c.GetVideosBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> batch, string _, CancellationToken _) =>
                batch.Select(id => new YouTubeVideoStat(id, "t", 1, 1, 1)).ToList());

        var result = await CreateService().PollAsync(Credential(), 5, CancellationToken.None);

        Assert.Equal(5, result.Value!.RecentVideos.Count);
    }

    [Fact]
    public async Task PollAsync_MapsPerVideoCounts_ToIntegerKeys()
    {
        SetupChannel(1, 1, 1);
        SetupUploads(["v1"]);
        _client.Setup(c => c.GetVideosBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new YouTubeVideoStat("v1", "My Video", 999, 88, 7)]);

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        var video = result.Value!.RecentVideos.Single();
        Assert.Equal("v1", video.VideoId);
        Assert.Equal("My Video", video.Title);
        Assert.Equal(999, video.Metrics["views"]);
        Assert.Equal(88, video.Metrics["likes"]);
        Assert.Equal(7, video.Metrics["comments"]);
        Assert.Equal(3, video.Metrics.Count);   // exactly the canonical per-video keys, no extras
    }

    [Fact]
    public async Task PollAsync_ApiError_ReturnsResultFail_NotThrow()
    {
        _client.Setup(c => c.GetChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task PollAsync_EmptyData_ReturnsSuccessWithEmptyMetrics()
    {
        SetupChannel(0, 0, 0);
        SetupUploads([]);

        var result = await CreateService().PollAsync(Credential(), 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.RecentVideos);
    }
}
