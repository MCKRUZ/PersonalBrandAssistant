using Microsoft.Extensions.Configuration;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Tests.Configuration;

public class ContentAutomationOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new ContentAutomationOptions();

        Assert.Equal("0 9 * * 1-5", options.CronExpression);
        Assert.Equal("Eastern Standard Time", options.TimeZone);
        Assert.True(options.Enabled);
        Assert.Equal(5, options.TopTrendsToConsider);
        Assert.Equal("SemiAuto", options.AutonomyLevel);
        Assert.Single(options.TargetPlatforms);
        Assert.Equal("LinkedIn", options.TargetPlatforms[0]);
    }

    [Fact]
    public void ImageGenerationDefaults_AreCorrect()
    {
        var options = new ImageGenerationOptions();

        Assert.True(options.Enabled);
        Assert.Equal("http://192.168.50.47:8188", options.ComfyUiBaseUrl);
        Assert.Equal(120, options.TimeoutSeconds);
        Assert.Equal(5, options.HealthCheckTimeoutSeconds);
        Assert.Equal(1536, options.DefaultWidth);
        Assert.Equal(1536, options.DefaultHeight);
        Assert.Equal(3, options.CircuitBreakerThreshold);
    }

    [Fact]
    public void BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ContentAutomation:CronExpression"] = "0 10 * * *",
                ["ContentAutomation:TimeZone"] = "UTC",
                ["ContentAutomation:Enabled"] = "false",
                ["ContentAutomation:TopTrendsToConsider"] = "3",
                ["ContentAutomation:ImageGeneration:ComfyUiBaseUrl"] = "http://localhost:8188",
                ["ContentAutomation:ImageGeneration:TimeoutSeconds"] = "60",
            })
            .Build();

        var options = new ContentAutomationOptions();
        config.GetSection("ContentAutomation").Bind(options);

        Assert.Equal("0 10 * * *", options.CronExpression);
        Assert.Equal("UTC", options.TimeZone);
        Assert.False(options.Enabled);
        Assert.Equal(3, options.TopTrendsToConsider);
        Assert.Equal("http://localhost:8188", options.ImageGeneration.ComfyUiBaseUrl);
        Assert.Equal(60, options.ImageGeneration.TimeoutSeconds);
    }
}
