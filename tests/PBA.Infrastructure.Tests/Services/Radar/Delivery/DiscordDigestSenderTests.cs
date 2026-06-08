using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Radar.Delivery;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar.Delivery;

public class DiscordDigestSenderTests
{
    private static readonly DeliveryNotification Sample = new(
        DeliveryKind.Digest, "Daily Brief", "Top stories today",
        new List<DeliveryItem> { new(1, 9, "Big AI story", "Ownable angle", "https://example.com/a") });

    private readonly Mock<HttpMessageHandler> _handler = new();

    private DiscordDigestSender Build(DiscordDeliveryOptions discord)
    {
        var http = new HttpClient(_handler.Object);
        var options = Options.Create(new DigestDeliveryOptions { Discord = discord });
        return new DiscordDigestSender(http, options, NullLogger<DiscordDigestSender>.Instance);
    }

    private void SetupResponse(HttpStatusCode status) =>
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status));

    private void CaptureRequest(out List<HttpRequestMessage> captured)
    {
        var list = new List<HttpRequestMessage>();
        captured = list;
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((r, _) => list.Add(r))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    [Fact]
    public void IsEnabled_ReflectsOptions()
    {
        Assert.True(Build(new DiscordDeliveryOptions { Enabled = true, WebhookUrl = "https://discord.com/api/webhooks/1/x" }).IsEnabled);
        Assert.False(Build(new DiscordDeliveryOptions { Enabled = false }).IsEnabled);
    }

    [Fact]
    public async Task SendAsync_PostsEmbedToConfiguredWebhook()
    {
        var sender = Build(new DiscordDeliveryOptions
        {
            Enabled = true, WebhookUrl = "https://discord.com/api/webhooks/123/abc"
        });
        CaptureRequest(out var captured);

        var result = await sender.SendAsync(Sample, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var req = Assert.Single(captured);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://discord.com/api/webhooks/123/abc", req.RequestUri!.ToString());
        var body = await req.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("embeds", out var embeds));
        Assert.NotEqual(0, embeds.GetArrayLength());
    }

    [Fact]
    public async Task SendAsync_NonDiscordHost_ReturnsFailAndMakesNoHttpCall()
    {
        var sender = Build(new DiscordDeliveryOptions
        {
            Enabled = true, WebhookUrl = "https://evil.example.com/api/webhooks/1/x"
        });
        SetupResponse(HttpStatusCode.NoContent);

        var result = await sender.SendAsync(Sample, CancellationToken.None);

        Assert.False(result.IsSuccess);
        _handler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_DiscordReturnsError_ReturnsFail()
    {
        var sender = Build(new DiscordDeliveryOptions
        {
            Enabled = true, WebhookUrl = "https://discord.com/api/webhooks/123/abc"
        });
        SetupResponse(HttpStatusCode.BadRequest);

        var result = await sender.SendAsync(Sample, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
