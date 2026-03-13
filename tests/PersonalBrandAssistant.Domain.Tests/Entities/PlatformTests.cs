using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class PlatformTests
{
    [Fact]
    public void Platform_WithAllRequiredFields_CreatesSuccessfully()
    {
        var platform = new Platform
        {
            Type = PlatformType.TwitterX,
            DisplayName = "Twitter/X",
            IsConnected = true,
        };

        Assert.Equal(PlatformType.TwitterX, platform.Type);
        Assert.Equal("Twitter/X", platform.DisplayName);
        Assert.True(platform.IsConnected);
        Assert.NotEqual(Guid.Empty, platform.Id);
    }

    [Fact]
    public void EncryptedTokens_AreByteArrays()
    {
        var platform = new Platform
        {
            Type = PlatformType.LinkedIn,
            DisplayName = "LinkedIn",
            EncryptedAccessToken = new byte[] { 1, 2, 3 },
            EncryptedRefreshToken = new byte[] { 4, 5, 6 },
        };

        Assert.IsType<byte[]>(platform.EncryptedAccessToken);
        Assert.IsType<byte[]>(platform.EncryptedRefreshToken);
    }
}
