using PersonalBrandAssistant.Infrastructure.BackgroundJobs;

namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;

public class RetryFailedProcessorTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    public void BackoffSchedule_MatchesExpectedDelays(int retryCount, int expectedMinutes)
    {
        // RetryFailedProcessor reuses ScheduledPublishProcessor.GetBackoffDelay
        var delay = ScheduledPublishProcessor.GetBackoffDelay(retryCount);
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), delay);
    }
}
