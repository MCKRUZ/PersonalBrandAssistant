namespace PersonalBrandAssistant.Application.Common.Models;

public class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";
    public string BaseUrl { get; set; } = "http://192.168.50.10:3002/v1";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxContentLength { get; set; } = 50_000;
}
