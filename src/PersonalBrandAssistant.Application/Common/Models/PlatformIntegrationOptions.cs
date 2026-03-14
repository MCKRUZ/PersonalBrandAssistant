namespace PersonalBrandAssistant.Application.Common.Models;

public class PlatformIntegrationOptions
{
    public PlatformOptions Twitter { get; set; } = new();
    public PlatformOptions LinkedIn { get; set; } = new();
    public PlatformOptions Instagram { get; set; } = new();
    public PlatformOptions YouTube { get; set; } = new();
}

public class PlatformOptions
{
    public string CallbackUrl { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? ApiVersion { get; set; }
    public int? DailyQuotaLimit { get; set; }
}
