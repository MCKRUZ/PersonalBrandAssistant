namespace PBA.Infrastructure.Configuration;

/// <summary>
/// External delivery for the AI News Radar (Phase 2). Ships dormant: every channel defaults
/// to Enabled=false. Secrets (SMTP password, webhook URL) come from user-secrets / Key Vault,
/// never from appsettings.
/// </summary>
public sealed class DigestDeliveryOptions
{
    public const string SectionName = "DigestDelivery";

    public EmailDeliveryOptions Email { get; init; } = new();
    public DiscordDeliveryOptions Discord { get; init; } = new();
    public AlertDeliveryOptions Alerts { get; init; } = new();
}

public sealed class EmailDeliveryOptions
{
    public bool Enabled { get; init; }
    public string SmtpHost { get; init; } = "";
    public int SmtpPort { get; init; } = 587;
    public bool UseStartTls { get; init; } = true;
    public string SmtpUser { get; init; } = "";
    public string SmtpPassword { get; init; } = "";
    public string FromAddress { get; init; } = "";
    public string FromName { get; init; } = "PBA Radar";
    public string ToAddress { get; init; } = "";
}

public sealed class DiscordDeliveryOptions
{
    public bool Enabled { get; init; }
    public string WebhookUrl { get; init; } = "";
}

public sealed class AlertDeliveryOptions
{
    public bool Enabled { get; init; }
    public int ScoreThreshold { get; init; } = 9;
    public int MaxPerDay { get; init; } = 5;
    public int SweepIntervalMinutes { get; init; } = 5;
}
