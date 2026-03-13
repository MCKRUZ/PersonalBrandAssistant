using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class BrandProfileTests
{
    [Fact]
    public void BrandProfile_WithValidFields_CreatesSuccessfully()
    {
        var profile = new BrandProfile
        {
            Name = "Tech Thought Leader",
            StyleGuidelines = "Professional, approachable",
            PersonaDescription = "Senior engineer sharing insights",
            IsActive = true,
        };

        Assert.Equal("Tech Thought Leader", profile.Name);
        Assert.NotEqual(Guid.Empty, profile.Id);
    }

    [Fact]
    public void ToneDescriptors_And_Topics_InitializeAsEmptyLists()
    {
        var profile = new BrandProfile();

        Assert.NotNull(profile.ToneDescriptors);
        Assert.Empty(profile.ToneDescriptors);
        Assert.NotNull(profile.Topics);
        Assert.Empty(profile.Topics);
    }
}
