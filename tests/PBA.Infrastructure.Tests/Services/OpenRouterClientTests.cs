using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services;
using Xunit;

namespace PBA.Infrastructure.Tests.Services;

public class OpenRouterClientTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CapturedBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"ok"}}]}""")
            };
        }
    }

    [Fact]
    public async Task SendPromptAsync_WithModelOverride_UsesOverrideModel()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var options = Options.Create(new OpenRouterOptions
        {
            ApiKey = "test-key",
            Model = "google/gemini-2.5-pro"
        });
        var client = new OpenRouterClient(http, options, NullLogger<OpenRouterClient>.Instance);

        await client.SendPromptAsync("sys", "user", model: "google/gemini-2.5-flash");

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.Equal("google/gemini-2.5-flash", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SendPromptAsync_WithoutModelOverride_UsesConfiguredModel()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var options = Options.Create(new OpenRouterOptions
        {
            ApiKey = "test-key",
            Model = "google/gemini-2.5-pro"
        });
        var client = new OpenRouterClient(http, options, NullLogger<OpenRouterClient>.Instance);

        await client.SendPromptAsync("sys", "user");

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        Assert.Equal("google/gemini-2.5-pro", doc.RootElement.GetProperty("model").GetString());
    }
}
