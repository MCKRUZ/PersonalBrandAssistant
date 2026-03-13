using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class UserTests
{
    [Fact]
    public void User_WithValidTimeZoneId_CreatesSuccessfully()
    {
        var user = new User
        {
            Email = "user@example.com",
            DisplayName = "Test User",
            TimeZoneId = "America/New_York",
        };

        Assert.Equal("America/New_York", user.TimeZoneId);
        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Fact]
    public void Settings_IsNotNullByDefault()
    {
        var user = new User();
        Assert.NotNull(user.Settings);
    }
}
