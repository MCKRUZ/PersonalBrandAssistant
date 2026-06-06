using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Infrastructure.Services.Radar.Delivery;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Radar.Delivery;

public class DeliveryDispatcherTests
{
    private static readonly DeliveryNotification Sample = new(
        DeliveryKind.Digest, "Title", "Intro",
        new List<DeliveryItem> { new(1, 9, "Headline", "Why", "https://x") });

    private static Mock<IDigestDeliverySender> Sender(string channel, bool enabled, Result? result = null)
    {
        var mock = new Mock<IDigestDeliverySender>();
        mock.SetupGet(s => s.Channel).Returns(channel);
        mock.SetupGet(s => s.IsEnabled).Returns(enabled);
        mock.Setup(s => s.SendAsync(It.IsAny<DeliveryNotification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result ?? Result.Success());
        return mock;
    }

    private static DeliveryDispatcher Build(params IDigestDeliverySender[] senders) =>
        new(senders, NullLogger<DeliveryDispatcher>.Instance);

    [Fact]
    public async Task DispatchAsync_CallsEnabledSendersOnly()
    {
        var enabled = Sender("email", enabled: true);
        var disabled = Sender("discord", enabled: false);
        var dispatcher = Build(enabled.Object, disabled.Object);

        await dispatcher.DispatchAsync(Sample, CancellationToken.None);

        enabled.Verify(s => s.SendAsync(Sample, It.IsAny<CancellationToken>()), Times.Once);
        disabled.Verify(s => s.SendAsync(It.IsAny<DeliveryNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_OneSenderThrows_DoesNotPropagateAndStillCallsOthers()
    {
        var throwing = Sender("email", enabled: true);
        throwing.Setup(s => s.SendAsync(It.IsAny<DeliveryNotification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        var healthy = Sender("discord", enabled: true);
        var dispatcher = Build(throwing.Object, healthy.Object);

        var ex = await Record.ExceptionAsync(() => dispatcher.DispatchAsync(Sample, CancellationToken.None));

        Assert.Null(ex);
        healthy.Verify(s => s.SendAsync(Sample, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_SenderReturnsFail_DoesNotThrowAndContinues()
    {
        var failing = Sender("email", enabled: true, result: Result.Fail("bad creds"));
        var healthy = Sender("discord", enabled: true);
        var dispatcher = Build(failing.Object, healthy.Object);

        var ex = await Record.ExceptionAsync(() => dispatcher.DispatchAsync(Sample, CancellationToken.None));

        Assert.Null(ex);
        healthy.Verify(s => s.SendAsync(Sample, It.IsAny<CancellationToken>()), Times.Once);
    }
}
