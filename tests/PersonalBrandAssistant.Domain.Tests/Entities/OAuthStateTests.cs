using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class OAuthStateTests
{
    [Fact]
    public void ExpiresAt_IsSetRelativeTo_CreatedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new OAuthState
        {
            State = "random-state",
            Platform = PlatformType.TwitterX,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(10),
        };

        Assert.True(state.ExpiresAt > state.CreatedAt);
        Assert.Equal(TimeSpan.FromMinutes(10), state.ExpiresAt - state.CreatedAt);
    }

    [Fact]
    public void OAuthState_StoresAllFields()
    {
        var state = new OAuthState
        {
            State = "csrf-state-token",
            Platform = PlatformType.TwitterX,
            CodeVerifier = "pkce-code-verifier",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        };

        Assert.Equal("csrf-state-token", state.State);
        Assert.Equal(PlatformType.TwitterX, state.Platform);
        Assert.Equal("pkce-code-verifier", state.CodeVerifier);
    }

    [Fact]
    public void OAuthState_GetsValidGuidId()
    {
        var state = new OAuthState
        {
            State = "test",
            Platform = PlatformType.LinkedIn,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        };

        Assert.NotEqual(Guid.Empty, state.Id);
    }
}
