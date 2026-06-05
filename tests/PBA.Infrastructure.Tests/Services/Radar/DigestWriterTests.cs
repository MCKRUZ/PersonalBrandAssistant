using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Radar;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class DigestWriterTests
{
    private static DigestWriter Build(string response, out Mock<ISidecarClient> sidecar)
    {
        sidecar = new Mock<ISidecarClient>();
        sidecar.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        return new DigestWriter(sidecar.Object, Options.Create(new DigestOptions { Model = "quality-model" }),
            NullLogger<DigestWriter>.Instance);
    }

    private static IReadOnlyList<DigestInput> Items() =>
        [new(0, "Story A", "Summary A", 9, "http://a"), new(1, "Story B", "Summary B", 7, null)];

    [Fact]
    public async Task WriteAsync_ValidJson_ReturnsCopy()
    {
        var writer = Build(
            """{"title":"Daily Brief","intro":"Today in AI.","items":[{"index":0,"whyItMatters":"Big deal."},{"index":1,"whyItMatters":"Notable."}]}""",
            out _);

        var copy = await writer.WriteAsync(Items());

        Assert.NotNull(copy);
        Assert.Equal("Daily Brief", copy!.Title);
        Assert.Equal(2, copy.Items.Count);
        Assert.Equal("Big deal.", copy.Items[0].WhyItMatters);
    }

    [Fact]
    public async Task WriteAsync_UsesQualityModel()
    {
        var writer = Build("""{"title":"t","intro":"i","items":[]}""", out var sidecar);
        await writer.WriteAsync(Items());
        sidecar.Verify(s => s.SendPromptAsync(
            It.IsAny<string>(), It.IsAny<string>(), "quality-model", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteAsync_StripsEmDashesFromOutput()
    {
        var writer = Build(
            """{"title":"Brief — Today","intro":"AI moves fast — keep up.","items":[{"index":0,"whyItMatters":"A — B"}]}""",
            out _);

        var copy = await writer.WriteAsync(Items());

        Assert.DoesNotContain('—', copy!.Title);
        Assert.DoesNotContain('—', copy.Intro);
        Assert.DoesNotContain('—', copy.Items[0].WhyItMatters);
    }

    [Fact]
    public async Task WriteAsync_MalformedJson_ReturnsNull()
    {
        var writer = Build("nope", out _);
        Assert.Null(await writer.WriteAsync(Items()));
    }
}
