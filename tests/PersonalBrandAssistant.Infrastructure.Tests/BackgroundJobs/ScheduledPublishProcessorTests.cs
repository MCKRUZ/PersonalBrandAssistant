using PersonalBrandAssistant.Infrastructure.BackgroundJobs;

namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;

public class ScheduledPublishProcessorTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(5, 15)]
    public void GetBackoffDelay_ReturnsExpectedMinutes(int retryCount, int expectedMinutes)
    {
        var delay = ScheduledPublishProcessor.GetBackoffDelay(retryCount);
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), delay);
    }

    [Fact]
    public void GetBackoffDelay_ClampsToMaximum()
    {
        var delay = ScheduledPublishProcessor.GetBackoffDelay(100);
        Assert.Equal(TimeSpan.FromMinutes(15), delay);
    }
}
