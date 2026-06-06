using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Radar.Delivery;

/// <summary>
/// Emails a radar notification over SMTP via MailKit. <see cref="BuildMessage"/> is the pure,
/// testable seam; the SMTP send itself is thin transport glue.
/// </summary>
public sealed class EmailDigestSender(
    IOptions<DigestDeliveryOptions> options,
    ILogger<EmailDigestSender> logger) : IDigestDeliverySender
{
    private readonly EmailDeliveryOptions _options = options.Value.Email;

    public string Channel => "email";

    public bool IsEnabled => _options.Enabled;

    public async Task<Result> SendAsync(DeliveryNotification notification, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost)
            || string.IsNullOrWhiteSpace(_options.FromAddress)
            || string.IsNullOrWhiteSpace(_options.ToAddress))
        {
            logger.LogWarning("Email delivery is enabled but SMTP host/from/to are not configured");
            return Result.Fail("Email delivery is not fully configured");
        }

        try
        {
            var message = BuildMessage(notification);
            using var client = new SmtpClient();
            // Both options require TLS: StartTls upgrades on the submission port and fails if the
            // server won't; SslOnConnect uses implicit TLS. Never fall back to cleartext, which
            // would leak SMTP credentials.
            var socketOptions = _options.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.SslOnConnect;
            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, socketOptions, ct);
            if (!string.IsNullOrEmpty(_options.SmtpUser))
                await client.AuthenticateAsync(new NetworkCredential(_options.SmtpUser, _options.SmtpPassword), ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "SMTP send failed");
            return Result.Fail("SMTP send failed");
        }
    }

    internal MimeMessage BuildMessage(DeliveryNotification n)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(_options.ToAddress));
        message.Subject = n.Kind == DeliveryKind.Alert ? $"[Radar Alert] {n.Title}" : n.Title;
        message.Body = new BodyBuilder { HtmlBody = RenderHtml(n) }.ToMessageBody();
        return message;
    }

    private static string RenderHtml(DeliveryNotification n)
    {
        var rows = string.Concat(n.Items.Select(i =>
        {
            var rank = i.Rank is { } r ? $"#{r} " : "";
            var headline = WebUtility.HtmlEncode(i.Headline);
            var why = WebUtility.HtmlEncode(i.WhyItMatters);
            var safeUrl = Uri.TryCreate(i.Url, UriKind.Absolute, out var u)
                && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
                ? u.AbsoluteUri
                : null;
            var link = safeUrl is null
                ? ""
                : $" <a href=\"{WebUtility.HtmlEncode(safeUrl)}\">read</a>";
            return $"<li><strong>{rank}{headline}</strong> ({i.Score}/10)<br/>{why}{link}</li>";
        }));

        return $"<h2>{WebUtility.HtmlEncode(n.Title)}</h2>"
             + $"<p>{WebUtility.HtmlEncode(n.Intro)}</p>"
             + $"<ol>{rows}</ol>";
    }
}
