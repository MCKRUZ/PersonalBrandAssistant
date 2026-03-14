using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Domain.Tests.ValueObjects;

public class PlatformRateLimitStateTests
{
    [Fact]
    public void Endpoints_DefaultsToEmptyDictionary()
    {
        var state = new PlatformRateLimitState();

        Assert.NotNull(state.Endpoints);
        Assert.Empty(state.Endpoints);
    }

    [Fact]
    public void CanTrackPerEndpointLimits()
    {
        var state = new PlatformRateLimitState();
        state.Endpoints["tweets/create"] = new EndpointRateLimit
        {
            RemainingCalls = 15,
            ResetAt = DateTimeOffset.UtcNow.AddMinutes(15),
        };

        Assert.Single(state.Endpoints);
        Assert.Equal(15, state.Endpoints["tweets/create"].RemainingCalls);
    }

    [Fact]
    public void DailyQuotaFields_DefaultToNull()
    {
        var state = new PlatformRateLimitState();

        Assert.Null(state.DailyQuotaUsed);
        Assert.Null(state.DailyQuotaLimit);
        Assert.Null(state.QuotaResetAt);
    }
}
