using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Radar;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class IdeaAnalyzerTests
{
    private static IdeaAnalyzer Build(string llmResponse, out Mock<ISidecarClient> sidecar)
    {
        sidecar = new Mock<ISidecarClient>();
        sidecar.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);
        var options = Options.Create(new IdeaScoringOptions { Model = "cheap-model" });
        return new IdeaAnalyzer(sidecar.Object, options, NullLogger<IdeaAnalyzer>.Instance);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidJson_ReturnsParsedAnalysis()
    {
        var analyzer = Build(
            """{"score":8,"reason":"Strong angle","summary":"One line","category":"AI","tags":["agents","enterprise"]}""",
            out _);

        var result = await analyzer.AnalyzeAsync("Title", "Desc", "http://x", "Source");

        Assert.NotNull(result);
        Assert.Equal(8, result!.Score);
        Assert.Equal("Strong angle", result.Reason);
        Assert.Equal("One line", result.Summary);
        Assert.Equal("AI", result.Category);
        Assert.Equal(new[] { "agents", "enterprise" }, result.Tags);
    }

    [Fact]
    public async Task AnalyzeAsync_PassesConfiguredCheapModel()
    {
        var analyzer = Build("""{"score":5,"reason":"r","summary":"s","category":null,"tags":[]}""", out var sidecar);

        await analyzer.AnalyzeAsync("T", null, null, "S");

        sidecar.Verify(s => s.SendPromptAsync(
            It.IsAny<string>(), It.IsAny<string>(), "cheap-model", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnalyzeAsync_MalformedJson_ReturnsNull()
    {
        var analyzer = Build("not json at all", out _);
        var result = await analyzer.AnalyzeAsync("T", null, null, "S");
        Assert.Null(result);
    }

    [Fact]
    public async Task AnalyzeAsync_FencedJson_StripsFencesAndParses()
    {
        var analyzer = Build("```json\n{\"score\":3,\"reason\":\"r\",\"summary\":\"s\",\"category\":null,\"tags\":[]}\n```", out _);
        var result = await analyzer.AnalyzeAsync("T", null, null, "S");
        Assert.NotNull(result);
        Assert.Equal(3, result!.Score);
    }

    [Fact]
    public async Task AnalyzeAsync_ScoreOutOfRange_ClampsTo0To10()
    {
        var analyzer = Build("""{"score":15,"reason":"r","summary":"s","category":null,"tags":[]}""", out _);
        var result = await analyzer.AnalyzeAsync("T", null, null, "S");
        Assert.Equal(10, result!.Score);
    }
}
