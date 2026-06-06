using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Radar.Delivery;

/// <summary>
/// Posts a radar notification to a Discord channel via an incoming webhook.
/// SSRF defense: the webhook host must be on Discord's known allowlist.
/// </summary>
public sealed class DiscordDigestSender(
    HttpClient http,
    IOptions<DigestDeliveryOptions> options,
    ILogger<DiscordDigestSender> logger) : IDigestDeliverySender
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.com", "discordapp.com", "canary.discord.com", "ptb.discord.com"
    };

    private readonly DiscordDeliveryOptions _options = options.Value.Discord;

    public string Channel => "discord";

    public bool IsEnabled => _options.Enabled;

    public async Task<Result> SendAsync(DeliveryNotification notification, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(_options.WebhookUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !AllowedHosts.Contains(uri.Host))
        {
            logger.LogWarning("Discord webhook URL is missing or not an allowed Discord host");
            return Result.Fail("Discord webhook URL is not a valid Discord host");
        }

        var payload = BuildPayload(notification);

        try
        {
            var response = await http.PostAsJsonAsync(uri, payload, ct);
            if (!response.IsSuccessStatusCode)
                return Result.Fail($"Discord webhook returned {(int)response.StatusCode}");
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Discord webhook POST failed");
            return Result.Fail("Discord webhook POST failed");
        }
    }

    private static object BuildPayload(DeliveryNotification n)
    {
        var fields = n.Items.Select(i => new
        {
            name = i.Rank is { } r ? $"#{r} · {i.Headline} ({i.Score}/10)" : $"{i.Headline} ({i.Score}/10)",
            value = string.IsNullOrWhiteSpace(i.Url) ? i.WhyItMatters : $"{i.WhyItMatters}\n{i.Url}",
            inline = false
        }).ToArray();

        return new
        {
            content = n.Kind == DeliveryKind.Alert ? $"🚨 {n.Title}" : null,
            embeds = new[]
            {
                new
                {
                    title = n.Title,
                    description = n.Intro,
                    fields
                }
            }
        };
    }
}
