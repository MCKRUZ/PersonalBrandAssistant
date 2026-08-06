namespace PBA.Infrastructure.Tests.Publishing;

/// <summary>
/// A TimeProvider stopped at a chosen instant. The quota-pacing rules turn on which side of a
/// Pacific midnight "now" falls, so tests that used the real clock would pass or fail depending on
/// the hour they were run — the kind of test that goes red on a Friday evening for no reason.
/// </summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
