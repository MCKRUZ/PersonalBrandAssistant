namespace PersonalBrandAssistant.Application.Common.Models;

public class ContentAutomationOptions
{
    public const string SectionName = "ContentAutomation";

    public string CronExpression { get; set; } = "0 9 * * 1-5";
    public string TimeZone { get; set; } = "America/New_York";
    public bool Enabled { get; set; } = true;
    public string AutonomyLevel { get; set; } = "SemiAuto";
    public int TopTrendsToConsider { get; set; } = 5;
    public string[] TargetPlatforms { get; set; } = ["LinkedIn"];
    public ImageGenerationOptions ImageGeneration { get; set; } = new();
    public PlatformPromptOptions PlatformPrompts { get; set; } = new();
}

public class ImageGenerationOptions
{
    public bool Enabled { get; set; } = true;
    public string ComfyUiBaseUrl { get; set; } = "http://192.168.50.47:8188";
    public string WorkflowTemplate { get; set; } = "flux-text-to-image";
    public int TimeoutSeconds { get; set; } = 120;
    public int HealthCheckTimeoutSeconds { get; set; } = 5;
    public int DefaultWidth { get; set; } = 1536;
    public int DefaultHeight { get; set; } = 1536;
    public string ModelCheckpoint { get; set; } = "flux1-dev-fp8.safetensors";
    public int CircuitBreakerThreshold { get; set; } = 3;
}

public class PlatformPromptOptions
{
    public string? LinkedIn { get; set; }
    public string? TwitterX { get; set; }
    public string? PersonalBlog { get; set; }
}
