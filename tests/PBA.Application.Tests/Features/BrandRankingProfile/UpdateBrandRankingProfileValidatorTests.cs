using PBA.Application.Features.BrandRankingProfile.Commands;
using Xunit;

namespace PBA.Application.Tests.Features.BrandRankingProfileApi;

public class UpdateBrandRankingProfileValidatorTests
{
    private static readonly UpdateBrandRankingProfileValidator Validator = new();

    private static UpdateBrandRankingProfile.Command Valid() => new()
    {
        Positioning = "Pos", AudiencePrimary = "Aud",
        HalfLifeDays = 7, DecayFloor = 0.075, AntiTopicMultiplier = 0.1, AuthorityBoost = 1.2,
        Pillars = [new UpdateBrandRankingProfile.PillarInput(Guid.NewGuid(), "P1", "D1", 0.5, 0)],
        ConcurrencyToken = "0"
    };

    [Fact]
    public void Valid_Command_Passes() => Assert.True(Validator.Validate(Valid()).IsValid);

    [Fact]
    public void PillarWeight_OutOfRange_Fails()
    {
        var cmd = Valid() with { Pillars = [new UpdateBrandRankingProfile.PillarInput(Guid.NewGuid(), "P", "D", 1.5, 0)] };
        Assert.False(Validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void NoPillars_Fails()
    {
        var cmd = Valid() with { Pillars = [] };
        Assert.False(Validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void EmptyPositioning_Fails()
    {
        var cmd = Valid() with { Positioning = "" };
        Assert.False(Validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void EmptyAudiencePrimary_Fails()
    {
        var cmd = Valid() with { AudiencePrimary = "" };
        Assert.False(Validator.Validate(cmd).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void HalfLifeDays_NotPositive_Fails(double halfLife)
    {
        var cmd = Valid() with { HalfLifeDays = halfLife };
        Assert.False(Validator.Validate(cmd).IsValid);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void DecayFloor_OutOfRange_Fails(double floor)
    {
        var cmd = Valid() with { DecayFloor = floor };
        Assert.False(Validator.Validate(cmd).IsValid);
    }

    [Fact]
    public void NonPositiveMultipliers_Fail()
    {
        Assert.False(Validator.Validate(Valid() with { AntiTopicMultiplier = 0 }).IsValid);
        Assert.False(Validator.Validate(Valid() with { AuthorityBoost = 0 }).IsValid);
    }
}
