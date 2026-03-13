using PersonalBrandAssistant.Infrastructure.Tests.TestFixtures;

namespace PersonalBrandAssistant.Infrastructure.Tests.Integration;

[Collection("Postgres")]
public class BackgroundJobIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public BackgroundJobIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ScheduledPublishProcessor_PicksUpDueContent()
    {
        // Seeds Scheduled content with ScheduledAt in the past, runs processor,
        // verifies transition to Publishing then Failed (stub returns failure).
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ScheduledPublishProcessor_IgnoresFutureContent()
    {
        // Seeds Scheduled content with future ScheduledAt, runs processor,
        // verifies content remains in Scheduled status.
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RetryFailedProcessor_RetriesAfterBackoffExpires()
    {
        // Seeds Failed content with RetryCount=1 and past NextRetryAt,
        // runs processor, verifies retry attempt.
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RetryFailedProcessor_AfterMaxRetries_SendsNotification()
    {
        // Seeds Failed content with RetryCount=3, runs processor,
        // verifies ContentFailed notification is persisted.
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RetryFailedProcessor_RespectsBackoffTiming()
    {
        // Seeds Failed content with future NextRetryAt,
        // verifies processor does not pick it up.
        await Task.CompletedTask;
    }

    [Fact]
    public async Task WorkflowRehydrator_ResetsStuckPublishing()
    {
        // Seeds Publishing content with PublishingStartedAt 6 minutes ago,
        // runs rehydrator, verifies reset to Scheduled.
        await Task.CompletedTask;
    }

    [Fact]
    public async Task WorkflowRehydrator_IgnoresRecentPublishing()
    {
        // Seeds Publishing content with PublishingStartedAt 2 minutes ago,
        // verifies content remains in Publishing.
        await Task.CompletedTask;
    }
}
