using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Radar;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class IdeaAnalyzerTests
{
    private static readonly Guid P1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid P2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static BrandRankingProfileSnapshot Profile() => new(
        Version: 3,
        Positioning: "Enterprise AI thought leader who ships",
        AudiencePrimary: "Senior engineers and architects",
        AudienceSecondary: "Executives and decision-makers",
        Pillars:
        [
            new BrandPillarSnapshot(P1, "Enterprise AI Adoption",
                "Strategy, governance, ROI of enterprise AI", 0.6, 0, null),
            new BrandPillarSnapshot(P2, "Agentic Development",
                "Building with agents, MCP, harnesses", 0.4, 1, null),
        ],
        AuthorityTopics: ["MCP server design"],
        AntiTopics: ["Crypto / web3 coins"],
        VoiceMarkers: ["No em dashes"],
        HalfLifeDays: 7, DecayFloor: 0.075, AntiTopicMultiplier: 0.1, AuthorityBoost: 1.2);

    private static IdeaAnalysisInput Input() => new("Title", "Desc", "http://x", "Source");

    private static IdeaAnalyzer Build(
        string response,
        out Mock<ISidecarClient> sidecar,
        out Mock<ILogger<IdeaAnalyzer>> logger,
        List<string>? capturedSystems = null,
        List<double?>? capturedTemps = null)
    {
        sidecar = new Mock<ISidecarClient>();
        sidecar.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, double?, CancellationToken>((sys, _, _, temp, _) =>
            {
                capturedSystems?.Add(sys);
                capturedTemps?.Add(temp);
            })
            .ReturnsAsync(response);
        logger = new Mock<ILogger<IdeaAnalyzer>>();
        var options = Options.Create(new IdeaScoringOptions { Model = "cheap-model" });
        return new IdeaAnalyzer(sidecar.Object, options, logger.Object);
    }

    private static void VerifyWarning(Mock<ILogger<IdeaAnalyzer>> logger, Times times) =>
        logger.Verify(l => l.Log(
            LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()), times);

    [Fact]
    public async Task AnalyzeAsync_StructuredJson_ParsesPerPillarSubScores()
    {
        var analyzer = Build(
            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"ownable"},{"name":"Agentic Development","score":0.25,"reason":"tangential"}],"isAntiTopic":false,"isAuthorityTopic":true,"reason":"good fit"}""",
            out _, out _);

        var result = await analyzer.AnalyzeAsync(Input(), Profile());

        Assert.NotNull(result);
        Assert.Equal(2, result!.Pillars.Count);
        var p1 = result.Pillars.Single(p => p.PillarId == P1);
        Assert.Equal(0.75, p1.Score);
        Assert.Equal("Enterprise AI Adoption", p1.PillarName);
        Assert.Equal(0.25, result.Pillars.Single(p => p.PillarId == P2).Score);
    }

    [Fact]
    public async Task AnalyzeAsync_LlmPillarNames_MapToBrandPillarId()
    {
        // Case + whitespace drift on the returned names must still map (R-C3).
        var analyzer = Build(
            """{"pillars":[{"name":"  enterprise ai adoption ","score":1,"reason":"r"},{"name":"AGENTIC DEVELOPMENT","score":0.5,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
            out _, out _);

        var result = await analyzer.AnalyzeAsync(Input(), Profile());

        Assert.NotNull(result);
        Assert.Equal(P1, result!.Pillars.Single(p => Math.Abs(p.Score - 1) < 1e-9).PillarId);
        Assert.Equal(P2, result.Pillars.Single(p => Math.Abs(p.Score - 0.5) < 1e-9).PillarId);
    }

    [Fact]
    public async Task AnalyzeAsync_UnknownPillarName_IsDroppedAndWarned()
    {
        var analyzer = Build(
            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"r"},{"name":"Totally Made Up","score":1,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
            out _, out var logger);

        var result = await analyzer.AnalyzeAsync(Input(), Profile());

        Assert.NotNull(result);
        Assert.Single(result!.Pillars);
        Assert.Equal(P1, result.Pillars[0].PillarId);
        VerifyWarning(logger, Times.AtLeastOnce());
    }

    [Fact]
    public async Task AnalyzeAsync_ExtractsFlagsAndPerPillarReason()
    {
        var analyzer = Build(
            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"ownable angle"},{"name":"Agentic Development","score":0.25,"reason":"tangential"}],"isAntiTopic":true,"isAuthorityTopic":true,"reason":"overall reason"}""",
            out _, out _);

        var result = await analyzer.AnalyzeAsync(Input(), Profile());

        Assert.NotNull(result);
        Assert.True(result!.IsAntiTopic);
        Assert.True(result.IsAuthorityTopic);
        Assert.Equal("overall reason", result.Reason);
        Assert.Equal("ownable angle", result.Pillars.Single(p => p.PillarId == P1).Reason);
    }

    [Fact]
    public async Task AnalyzeAsync_SystemPrompt_IsBuiltFromSnapshot()
    {
        var systems = new List<string>();
        var analyzer = Build(
            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"r"},{"name":"Agentic Development","score":0.25,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
            out _, out _, capturedSystems: systems);

        await analyzer.AnalyzeAsync(Input(), Profile());

        var system = Assert.Single(systems);
        Assert.Contains("Enterprise AI thought leader who ships", system);   // positioning
        Assert.Contains("Senior engineers and architects", system);          // audience
        Assert.Contains("Enterprise AI Adoption", system);                   // pillar name
        Assert.Contains("Strategy, governance, ROI of enterprise AI", system); // pillar description
        Assert.Contains("Agentic Development", system);
        Assert.Contains("MCP server design", system);                        // authority topic
        Assert.Contains("Crypto / web3 coins", system);                      // anti topic
        Assert.Contains("0.75 = clearly relevant", system);                  // per-level rubric
    }

    [Fact]
    public async Task AnalyzeAsync_IncludesFixedFewShotAnchors_StableAcrossCalls()
    {
        var systems = new List<string>();
        var analyzer = Build(
            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"r"},{"name":"Agentic Development","score":0.25,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
            out _, out _, capturedSystems: systems);

        await analyzer.AnalyzeAsync(new IdeaAnalysisInput("First", "a", null, "S1"), Profile());
        await analyzer.AnalyzeAsync(new IdeaAnalysisInput("Second", "b", null, "S2"), Profile());

        Assert.Equal(2, systems.Count);
        Assert.Equal(systems[0], systems[1]); // system prompt depends only on the profile, not the input
        var idxA = systems[0].IndexOf("Example A", StringComparison.Ordinal);
        var idxB = systems[0].IndexOf("Example B", StringComparison.Ordinal);
        Assert.True(idxA >= 0 && idxB > idxA); // both anchors present, fixed order
    }

    [Fact]
    public async Task AnalyzeAsync_NotJson_ReturnsNull()
    {
        var analyzer = Build("not json at all", out _, out _);
        Assert.Null(await analyzer.AnalyzeAsync(Input(), Profile()));
    }

    [Fact]
    public async Task AnalyzeAsync_MissingPillarsArray_ReturnsNull()
    {
        var analyzer = Build("""{"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""", out _, out _);
        Assert.Null(await analyzer.AnalyzeAsync(Input(), Profile()));
    }

    [Fact]
    public async Task AnalyzeAsync_FencedJson_StripsFencesAndParses()
    {
        var analyzer = Build(
            "```json\n{\"pillars\":[{\"name\":\"Enterprise AI Adoption\",\"score\":1,\"reason\":\"r\"},{\"name\":\"Agentic Development\",\"score\":0,\"reason\":\"r\"}],\"isAntiTopic\":false,\"isAuthorityTopic\":false,\"reason\":\"r\"}\n```",
            out _, out _);

        var result = await analyzer.AnalyzeAsync(Input(), Profile());

        Assert.NotNull(result);
        Assert.Equal(2, result!.Pillars.Count);
    }

    [Fact]
    public async Task AnalyzeAsync_AllPillarScoresIdentical_IsRejectedAndWarned()
    {
        var analyzer = Build(
            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.5,"reason":"r"},{"name":"Agentic Development","score":0.5,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
            out _, out var logger);

        var result = await analyzer.AnalyzeAsync(Input(), Profile());

        Assert.Null(result); // central-collapse guard (R-M5)
        VerifyWarning(logger, Times.AtLeastOnce());
    }

    [Fact]
    public async Task AnalyzeAsync_PassesLowTemperatureAndCheapModel()
    {
        var temps = new List<double?>();
        var analyzer = Build(
            """{"pillars":[{"name":"Enterprise AI Adoption","score":0.75,"reason":"r"},{"name":"Agentic Development","score":0.25,"reason":"r"}],"isAntiTopic":false,"isAuthorityTopic":false,"reason":"r"}""",
            out var sidecar, out _, capturedTemps: temps);

        await analyzer.AnalyzeAsync(Input(), Profile());

        var temp = Assert.Single(temps);
        Assert.True(temp is >= 0 and <= 0.2, $"temperature {temp} must be in [0, 0.2]");
        sidecar.Verify(s => s.SendPromptAsync(
            It.IsAny<string>(), It.IsAny<string>(), "cheap-model", It.IsAny<double?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
