using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Infrastructure.Tests.Publishing;

public class ComfyUiHeroImageGeneratorTests
{
    private static BlogPostMeta CreatePost(string slug = "my-post", string title = "Agents in the Enterprise") =>
        new(
            Slug: slug,
            Title: title,
            Excerpt: "An excerpt.",
            Category: "Enterprise AI",
            Date: new DateOnly(2026, 6, 1),
            HeroImagePath: $"assets/blog-images/{slug}.png",
            Url: $"https://matthewkruczek.ai/posts/{slug}");

    [Fact]
    public void BuildPrompt_IncludesTitleAndStyleKeywords()
    {
        var prompt = ComfyUiHeroImageGenerator.BuildPrompt(CreatePost(title: "Agentic Workflows"));

        Assert.Contains("Agentic Workflows", prompt);
        Assert.Contains("Enterprise AI", prompt);
        Assert.Contains("#0f1117", prompt);
        Assert.Contains("#d4a853", prompt);
        Assert.Contains("no text", prompt);
    }

    [Fact]
    public void InjectPrompt_SetsTargetNodeTextAndLeavesOthersUntouched()
    {
        const string workflow = """
        {"3":{"inputs":{"text":"OLD"}},"7":{"inputs":{"text":"NEGATIVE","seed":42}}}
        """;

        var result = ComfyUiHeroImageGenerator.InjectPrompt(workflow, "NEW PROMPT", "3");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("NEW PROMPT", doc.RootElement.GetProperty("3").GetProperty("inputs").GetProperty("text").GetString());
        Assert.Equal("NEGATIVE", doc.RootElement.GetProperty("7").GetProperty("inputs").GetProperty("text").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("7").GetProperty("inputs").GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task GenerateAsync_HappyPath_WritesFileAndReturnsSuccess()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pba-comfy-tests", Guid.NewGuid().ToString("N"));
        var workflowPath = Path.Combine(tempDir, "workflow.json");
        var outputDir = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(workflowPath, """{"3":{"inputs":{"text":"OLD"}},"9":{"inputs":{}}}""");

        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var handler = new RoutingHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/prompt")
                return Json(new { prompt_id = "abc-123" });

            if (path == "/history/abc-123")
                return Json(BuildHistory("abc-123"));

            if (path == "/view")
                return Binary(pngBytes);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var generator = CreateGenerator(handler, new ComfyUiOptions
        {
            Enabled = true,
            BaseUrl = "http://comfy.test",
            WorkflowPath = workflowPath,
            PromptNodeId = "3",
            OutputDirectory = outputDir,
            PollIntervalMs = 1,
            TimeoutMs = 5000
        });

        var result = await generator.GenerateAsync(CreatePost(slug: "hero-test"), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join(",", result.Errors));
        var expectedPath = Path.Combine(outputDir, "hero-test.png");
        Assert.Equal(expectedPath, result.Value);
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(pngBytes, await File.ReadAllBytesAsync(expectedPath));

        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task GenerateAsync_NotEnabled_ReturnsCleanFailWithoutHttp()
    {
        var handler = new RoutingHandler(_ =>
            throw new InvalidOperationException("HTTP must not be called when disabled"));

        var generator = CreateGenerator(handler, new ComfyUiOptions
        {
            Enabled = false,
            WorkflowPath = "ignored.json"
        });

        var result = await generator.GenerateAsync(CreatePost(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not configured", result.Errors[0]);
        Assert.Equal(0, handler.CallCount);
    }

    private static ComfyUiHeroImageGenerator CreateGenerator(HttpMessageHandler handler, ComfyUiOptions opts)
    {
        var httpClient = new HttpClient(handler);
        var monitor = new Mock<IOptionsMonitor<ComfyUiOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(opts);
        return new ComfyUiHeroImageGenerator(
            httpClient,
            monitor.Object,
            NullLogger<ComfyUiHeroImageGenerator>.Instance);
    }

    // Builds the ComfyUI /history/{id} response shape:
    // { "<promptId>": { "outputs": { "9": { "images": [ { filename, subfolder, type } ] } } } }
    private static object BuildHistory(string promptId)
    {
        var image = new { filename = "hero_00001_.png", subfolder = "", type = "output" };
        var node = new { images = new[] { image } };
        var outputs = new Dictionary<string, object> { ["9"] = node };
        var entry = new { outputs };
        return new Dictionary<string, object> { [promptId] = entry };
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Binary(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes)
    };

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(route(request));
        }
    }
}
