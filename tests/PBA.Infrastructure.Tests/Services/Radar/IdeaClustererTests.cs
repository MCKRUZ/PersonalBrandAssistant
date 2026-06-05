using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Radar;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar;

public class IdeaClustererTests
{
    private static IdeaClusterer Build(string llmResponse)
    {
        var sidecar = new Mock<ISidecarClient>();
        sidecar.Setup(s => s.SendPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);
        return new IdeaClusterer(sidecar.Object, Options.Create(new ClusteringOptions()),
            NullLogger<IdeaClusterer>.Instance);
    }

    private static IReadOnlyList<ClusterInput> Items() =>
    [
        new(0, "GPT-5 released", "OpenAI ships GPT-5"),
        new(1, "OpenAI launches GPT-5", "New flagship model"),
        new(2, "Unrelated story", "Something else")
    ];

    [Fact]
    public async Task ClusterAsync_ValidGroups_ReturnsGroups()
    {
        var clusterer = Build("""{"groups":[[0,1]]}""");
        var groups = await clusterer.ClusterAsync(Items());
        Assert.Single(groups);
        Assert.Equal(new[] { 0, 1 }, groups[0]);
    }

    [Fact]
    public async Task ClusterAsync_MalformedJson_ReturnsEmpty()
    {
        var clusterer = Build("garbage");
        var groups = await clusterer.ClusterAsync(Items());
        Assert.Empty(groups);
    }

    [Fact]
    public async Task ClusterAsync_FewerThanTwoItems_DoesNotCallLlm()
    {
        var sidecar = new Mock<ISidecarClient>();
        var clusterer = new IdeaClusterer(sidecar.Object, Options.Create(new ClusteringOptions()),
            NullLogger<IdeaClusterer>.Instance);

        var groups = await clusterer.ClusterAsync([new(0, "only one", null)]);

        Assert.Empty(groups);
        sidecar.Verify(s => s.SendPromptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
