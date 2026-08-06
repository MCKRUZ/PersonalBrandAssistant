using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Connectors;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class BufferConnectorTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly Mock<IMediaHost> _mediaHost = new();
    private readonly Mock<ILogger<BufferConnector>> _logger = new();

    private readonly BufferOptions _options = new()
    {
        Enabled = true,
        ApiKey = "test-buffer-key",
        Endpoint = "https://api.buffer.com",
        DiscloseAiGenerated = true
    };

    private string? _capturedCreateBody;

    public BufferConnectorTests()
    {
        _httpClient = new HttpClient(_httpHandler.Object)
        {
            BaseAddress = new Uri("https://api.buffer.com")
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        _mediaHost.Setup(m => m.UploadAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedMedia("https://media.matthewkruczek.ai/tiktok/x.mp4", "tiktok/x.mp4"));

        // The schedule cap now comes from the media host rather than a constant here, so the same
        // number governs Buffer and the posts PBA holds itself — one bucket, one lifecycle rule.
        _mediaHost.Setup(m => m.MaxHostedLifetime).Returns(TimeSpan.FromDays(7));
    }

    private BufferConnector CreateConnector()
    {
        var monitor = new Mock<IOptionsMonitor<BufferOptions>>();
        monitor.Setup(o => o.CurrentValue).Returns(_options);
        return new BufferConnector(_httpClient, _mediaHost.Object, monitor.Object, _logger.Object);
    }

    private static Content CreateContent(string title = "Test Clip") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Body = "Test body content",
        Status = ContentStatus.Approved,
        PrimaryPlatform = Platform.TikTok
    };

    private static MediaAttachment VideoMedia() =>
        new(new byte[16], "clip.mp4", "video/mp4", "A clip");

    // A minimal but valid MP4 (ftyp + moov>mvhd) reporting the given duration, so the connector can
    // read it and compute a mid-clip cover offset.
    private static MediaAttachment Mp4Media(int durationMs)
    {
        static byte[] Box(string type, byte[] payload)
        {
            var box = new byte[8 + payload.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
            for (var i = 0; i < 4; i++) box[4 + i] = (byte)type[i];
            payload.CopyTo(box, 8);
            return box;
        }

        var mvhdPayload = new byte[20];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(mvhdPayload.AsSpan(12, 4), 1000); // timescale
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(mvhdPayload.AsSpan(16, 4), (uint)durationMs);
        var moov = Box("moov", Box("mvhd", mvhdPayload));
        return new MediaAttachment(moov, "clip.mp4", "video/mp4", "A clip");
    }

    /// <summary>
    /// All Buffer operations POST to the same root endpoint, so branch on the GraphQL query text in
    /// the request body: an org lookup, a channels lookup, or the createPost mutation.
    /// </summary>
    private void SetupGraphQl(bool mutationError = false, string errorMessage = "channel is not connected")
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                var body = await req.Content!.ReadAsStringAsync();

                if (body.Contains("createPost"))
                {
                    _capturedCreateBody = body;
                    if (mutationError)
                        return Json(new
                        {
                            data = new
                            {
                                createPost = new { __typename = "MutationError", message = errorMessage }
                            }
                        });

                    return Json(new
                    {
                        data = new
                        {
                            createPost = new
                            {
                                __typename = "PostActionSuccess",
                                post = new { id = "post-abc" }
                            }
                        }
                    });
                }

                if (body.Contains("channels"))
                    return Json(new
                    {
                        data = new
                        {
                            channels = new[]
                            {
                                new { id = "chan-fb", name = "FB Page", service = "facebook" },
                                new { id = "chan-tiktok", name = "My TikTok", service = "tiktok" }
                            }
                        }
                    });

                // account { organizations }
                return Json(new
                {
                    data = new
                    {
                        account = new
                        {
                            organizations = new[] { new { id = "org-1", name = "Org" } }
                        }
                    }
                });
            });
    }

    [Fact]
    public async Task PublishAsync_VideoNoSchedule_BuildsShareNowMutationWithUrlAndChannel()
    {
        SetupGraphQl();
        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Watch this clip!", [], null, PublishMode.Publish, null, VideoMedia());

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("post-abc", result.PlatformPostId);
        Assert.Null(result.PublishedUrl); // Buffer returns no public TikTok URL at create time

        Assert.NotNull(_capturedCreateBody);
        Assert.Contains("https://media.matthewkruczek.ai/tiktok/x.mp4", _capturedCreateBody);
        Assert.Contains("chan-tiktok", _capturedCreateBody);
        Assert.Contains("shareNow", _capturedCreateBody);
        Assert.Contains("Watch this clip!", _capturedCreateBody);
        Assert.Contains("isAiGenerated", _capturedCreateBody); // AI disclosure flag present
        Assert.DoesNotContain("customScheduled", _capturedCreateBody);

        _mediaHost.Verify(m => m.UploadAsync(
            It.IsAny<byte[]>(), "clip.mp4", "video/mp4", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishAsync_VideoScheduled_BuildsCustomScheduledWithIsoDueAt()
    {
        SetupGraphQl();
        var connector = CreateConnector();
        var scheduledAt = new DateTimeOffset(2026, 7, 27, 14, 15, 0, TimeSpan.Zero);
        var request = new PlatformPublishRequest(
            CreateContent(), "Scheduled clip", [], null, PublishMode.Publish, scheduledAt, VideoMedia());

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(_capturedCreateBody);
        Assert.Contains("customScheduled", _capturedCreateBody);
        Assert.Contains("\"dueAt\":\"2026-07-27T14:15:00Z\"", _capturedCreateBody);
        Assert.DoesNotContain("shareNow", _capturedCreateBody);
    }

    [Fact]
    public async Task PublishAsync_Mp4Video_SetsMidClipCoverOffset()
    {
        SetupGraphQl();
        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Clip", [], null, PublishMode.Publish, null, Mp4Media(60_000));

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(_capturedCreateBody);
        // 60s clip * default 0.5 fraction => 30000ms cover, never the black frame-0.
        Assert.Contains("\"thumbnailOffset\":30000", _capturedCreateBody);
    }

    [Fact]
    public async Task PublishAsync_UnreadableVideo_UsesNonZeroFallbackCoverOffset()
    {
        SetupGraphQl();
        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Clip", [], null, PublishMode.Publish, null, VideoMedia());

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(_capturedCreateBody);
        Assert.Contains("\"thumbnailOffset\":2000", _capturedCreateBody);
        Assert.DoesNotContain("\"thumbnailOffset\":0", _capturedCreateBody);
    }

    [Fact]
    public async Task PublishAsync_MutationError_ReturnsFailureWithMessage()
    {
        SetupGraphQl(mutationError: true, errorMessage: "TikTok channel is disconnected");
        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Clip", [], null, PublishMode.Publish, null, VideoMedia());

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("TikTok channel is disconnected", result.ErrorMessage);
    }

    [Fact]
    public async Task PublishAsync_NoMedia_ReturnsFailureWithoutUploading()
    {
        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Clip", [], null, PublishMode.Publish, null, Media: null);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("requires a video", result.ErrorMessage!);
        _mediaHost.Verify(m => m.UploadAsync(
            It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_NonVideoMedia_ReturnsFailureWithoutUploading()
    {
        var connector = CreateConnector();
        var image = new MediaAttachment(new byte[16], "thumb.png", "image/png", null);
        var request = new PlatformPublishRequest(
            CreateContent(), "Clip", [], null, PublishMode.Publish, null, image);

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("requires a video", result.ErrorMessage!);
        _mediaHost.Verify(m => m.UploadAsync(
            It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_ScheduledBeyondSevenDays_ReturnsFailureWithoutUploading()
    {
        var connector = CreateConnector();
        var request = new PlatformPublishRequest(
            CreateContent(), "Clip", [], null, PublishMode.Publish,
            DateTimeOffset.UtcNow.AddDays(8), VideoMedia());

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("7 days", result.ErrorMessage!);
        _mediaHost.Verify(m => m.UploadAsync(
            It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void GetCapabilities_ReturnsCorrectValues()
    {
        var caps = CreateConnector().GetCapabilities();

        Assert.Equal(2200, caps.MaxCharacters);
        Assert.False(caps.SupportsMarkdown);
        Assert.False(caps.SupportsHtml);
        Assert.False(caps.SupportsImages);
        Assert.True(caps.SupportsScheduling);
        Assert.False(caps.SupportsThreads);
        Assert.Contains("video/mp4", caps.SupportedMediaTypes);
        Assert.Contains("video/quicktime", caps.SupportedMediaTypes);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_OrgsReturned_ReturnsTrue()
    {
        SetupGraphQl();
        var connector = CreateConnector();

        var result = await connector.ValidateCredentialsAsync(CancellationToken.None);

        Assert.True(result);
    }

    // A valid API key with no TikTok channel connected publishes nothing, so reporting "ready" on
    // the key alone is a false green — callers schedule against it and find out at the slot.
    [Fact]
    public async Task ValidateCredentialsAsync_KeyValidButNoTikTokChannel_ReturnsFalse()
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                var body = await req.Content!.ReadAsStringAsync();
                if (body.Contains("channels"))
                    return Json(new
                    {
                        data = new
                        {
                            channels = new[] { new { id = "chan-fb", name = "FB Page", service = "facebook" } }
                        }
                    });

                return Json(new
                {
                    data = new
                    {
                        account = new { organizations = new[] { new { id = "org-1", name = "Org" } } }
                    }
                });
            });

        var connector = CreateConnector();

        var result = await connector.ValidateCredentialsAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_HttpError_ReturnsFalse()
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var connector = CreateConnector();

        var result = await connector.ValidateCredentialsAsync(CancellationToken.None);

        Assert.False(result);
    }

    private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json")
    };

    public void Dispose() => _httpClient.Dispose();
}
