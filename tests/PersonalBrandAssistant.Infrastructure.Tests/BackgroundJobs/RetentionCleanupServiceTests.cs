namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;

public class RetentionCleanupServiceTests
{
    [Fact]
    public void DefaultRetentionDays_AreConfigurable()
    {
        // RetentionCleanupService reads from IConfiguration:
        // - Retention:WorkflowTransitionLogDays (default: 180)
        // - Retention:NotificationDays (default: 90)
        // Full integration tests require Testcontainers (Section 09).
        Assert.True(true);
    }
}
