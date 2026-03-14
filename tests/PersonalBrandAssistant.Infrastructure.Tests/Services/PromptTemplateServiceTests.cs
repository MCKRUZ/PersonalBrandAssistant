using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

public class PromptTemplateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IHostEnvironment> _hostEnv;
    private readonly Mock<ILogger<PromptTemplateService>> _logger;

    public PromptTemplateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"prompt_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _hostEnv = new Mock<IHostEnvironment>();
        _hostEnv.Setup(e => e.EnvironmentName).Returns("Production");

        _logger = new Mock<ILogger<PromptTemplateService>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private PromptTemplateService CreateService() =>
        new(_tempDir, _hostEnv.Object, _logger.Object);

    private void WriteTemplate(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public async Task RenderAsync_LoadsTemplateFromCorrectPath()
    {
        WriteTemplate("writer/blog-post.liquid", "Hello {{ name }}");
        var service = CreateService();

        var result = await service.RenderAsync("writer", "blog-post",
            new Dictionary<string, object> { ["name"] = "World" });

        Assert.Equal("Hello World", result);
    }

    [Fact]
    public async Task RenderAsync_InjectsBrandVoiceBlock()
    {
        WriteTemplate("shared/brand-voice.liquid", "I am {{ brand.Name }}, the brand voice.");
        WriteTemplate("writer/system.liquid", "Voice: {{ brand_voice_block }}");
        var service = CreateService();

        var brand = new BrandProfilePromptModel
        {
            Name = "TestBrand",
            PersonaDescription = "A test persona",
            ToneDescriptors = ["professional"],
            StyleGuidelines = "Keep it short",
            PreferredTerms = ["innovation"],
            AvoidedTerms = ["synergy"],
            Topics = ["tech"],
            ExampleContent = ["Example post"]
        };

        var result = await service.RenderAsync("writer", "system",
            new Dictionary<string, object> { ["brand"] = brand });

        Assert.Contains("I am TestBrand, the brand voice.", result);
    }

    [Fact]
    public async Task RenderAsync_RendersVariablesIntoTemplate()
    {
        WriteTemplate("writer/test.liquid", "{{ brand.Name }} writes about {{ brand.Topics | join: ', ' }}");
        var service = CreateService();

        var brand = new BrandProfilePromptModel
        {
            Name = "Matt",
            PersonaDescription = "AI expert",
            ToneDescriptors = ["authoritative"],
            StyleGuidelines = "Direct",
            PreferredTerms = ["AI"],
            AvoidedTerms = ["buzzword"],
            Topics = ["AI", "Leadership"],
            ExampleContent = []
        };

        var result = await service.RenderAsync("writer", "test",
            new Dictionary<string, object> { ["brand"] = brand });

        Assert.Contains("Matt", result);
        Assert.Contains("AI, Leadership", result);
    }

    [Fact]
    public async Task RenderAsync_CachesParsedTemplates_SecondCallDoesNotReReadFile()
    {
        WriteTemplate("writer/cached.liquid", "Cached: {{ val }}");
        var service = CreateService();

        var vars = new Dictionary<string, object> { ["val"] = "first" };
        var first = await service.RenderAsync("writer", "cached", vars);
        Assert.Equal("Cached: first", first);

        // Delete the file — cache should still serve the template
        File.Delete(Path.Combine(_tempDir, "writer/cached.liquid"));

        vars["val"] = "second";
        var second = await service.RenderAsync("writer", "cached", vars);
        Assert.Equal("Cached: second", second);
    }

    [Fact]
    public async Task RenderAsync_ThrowsWhenTemplateFileNotFound()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.RenderAsync("writer", "nonexistent", new Dictionary<string, object>()));
    }

    [Fact]
    public void ListTemplates_ReturnsAllLiquidFilesForAgent()
    {
        WriteTemplate("writer/system.liquid", "sys");
        WriteTemplate("writer/blog-post.liquid", "blog");
        WriteTemplate("writer/article.liquid", "article");
        var service = CreateService();

        var templates = service.ListTemplates("writer");

        Assert.Equal(3, templates.Length);
        Assert.Contains("article", templates);
        Assert.Contains("blog-post", templates);
        Assert.Contains("system", templates);
    }

    [Fact]
    public async Task RenderAsync_UsesPromptViewModelDTOs()
    {
        WriteTemplate("writer/full.liquid", "Brand: {{ brand.Name }}, Content: {{ content.Title }}, Type: {{ content.ContentType }}");
        var service = CreateService();

        var brand = new BrandProfilePromptModel
        {
            Name = "TestBrand",
            PersonaDescription = "desc",
            ToneDescriptors = ["casual"],
            StyleGuidelines = "none",
            PreferredTerms = [],
            AvoidedTerms = [],
            Topics = [],
            ExampleContent = []
        };

        var content = new ContentPromptModel
        {
            Title = "My Post",
            Body = "Body text",
            ContentType = ContentType.BlogPost,
            Status = ContentStatus.Draft,
            TargetPlatforms = [PlatformType.LinkedIn]
        };

        var result = await service.RenderAsync("writer", "full",
            new Dictionary<string, object> { ["brand"] = brand, ["content"] = content });

        Assert.Contains("Brand: TestBrand", result);
        Assert.Contains("Content: My Post", result);
    }

    [Fact]
    public void ListTemplates_ReturnsEmptyForNonexistentAgent()
    {
        var service = CreateService();

        var templates = service.ListTemplates("nonexistent");

        Assert.Empty(templates);
    }

    [Theory]
    [InlineData("../etc", "blog-post")]
    [InlineData("writer", "../../appsettings")]
    [InlineData("writer/../../", "secret")]
    public async Task RenderAsync_RejectsPathTraversalAttempts(string agentName, string templateName)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RenderAsync(agentName, templateName, new Dictionary<string, object>()));
    }

    [Fact]
    public async Task RenderAsync_ThrowsOnMalformedTemplate()
    {
        WriteTemplate("writer/bad.liquid", "{% if unclosed %}");
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RenderAsync("writer", "bad", new Dictionary<string, object>()));
    }
}
