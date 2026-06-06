using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Radar.Delivery;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar.Delivery;

public class EmailDigestSenderTests
{
    private static readonly DeliveryNotification Sample = new(
        DeliveryKind.Digest, "Daily Brief", "Top stories today",
        new List<DeliveryItem>
        {
            new(1, 9, "Big AI story", "Strong ownable angle", "https://example.com/a"),
            new(2, 8, "Second story", "Worth a take", "https://example.com/b")
        });

    private static EmailDigestSender Build(EmailDeliveryOptions email) =>
        new(Options.Create(new DigestDeliveryOptions { Email = email }), NullLogger<EmailDigestSender>.Instance);

    private static EmailDeliveryOptions ValidEmail() => new()
    {
        Enabled = true, SmtpHost = "smtp.example.com", SmtpUser = "u", SmtpPassword = "p",
        FromAddress = "radar@example.com", FromName = "PBA Radar", ToAddress = "matt@example.com"
    };

    [Fact]
    public void IsEnabled_ReflectsOptions()
    {
        Assert.True(Build(ValidEmail()).IsEnabled);
        Assert.False(Build(new EmailDeliveryOptions { Enabled = false }).IsEnabled);
    }

    [Fact]
    public void BuildMessage_SetsRecipientsSubjectAndHtmlBody()
    {
        var msg = Build(ValidEmail()).BuildMessage(Sample);

        Assert.Contains("Daily Brief", msg.Subject);
        Assert.Equal("matt@example.com", msg.To.Mailboxes.Single().Address);
        Assert.Equal("radar@example.com", msg.From.Mailboxes.Single().Address);

        var html = msg.HtmlBody;
        Assert.Contains("Big AI story", html);
        Assert.Contains("Second story", html);
        Assert.Contains("https://example.com/a", html);
        Assert.Contains("Top stories today", html);
    }

    [Fact]
    public void BuildMessage_NonHttpUrl_DropsLinkInsteadOfRenderingIt()
    {
        var malicious = new DeliveryNotification(DeliveryKind.Alert, "Alert", "intro",
            new List<DeliveryItem> { new(null, 9, "Story", "Why", "javascript:alert(1)") });

        var html = Build(ValidEmail()).BuildMessage(malicious).HtmlBody;

        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("<a ", html); // no anchor emitted for an unsafe scheme
    }

    [Fact]
    public async Task SendAsync_NotConfigured_ReturnsFailWithoutThrowing()
    {
        var sender = Build(new EmailDeliveryOptions { Enabled = true }); // no host/from/to

        var result = await sender.SendAsync(Sample, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
