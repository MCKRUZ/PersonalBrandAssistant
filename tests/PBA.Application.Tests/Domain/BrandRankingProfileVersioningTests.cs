using PBA.Domain.Entities;
using Xunit;

namespace PBA.Application.Tests.FeedRanking;

public class BrandRankingProfileVersioningTests
{
    private static BrandRankingProfile Make(out Guid pillarId)
    {
        pillarId = Guid.NewGuid();
        var pid = pillarId;
        return new BrandRankingProfile
        {
            Positioning = "I show enterprise teams what AI can actually ship.",
            AudiencePrimary = "Senior engineers and architects.",
            AudienceSecondary = "Enterprise executives.",
            AuthorityTopics = [".NET in enterprise AI", "MCP server design"],
            AntiTopics = ["crypto", "AGI doomerism"],
            VoiceMarkers = ["data > opinion"],
            Pillars = [new BrandPillar { Id = pid, Name = "Agent-Native", Description = "harnesses, skills, MCP", Weight = 0.5, Order = 0 }],
            HalfLifeDays = 7,
            DecayFloor = 0.075,
            AntiTopicMultiplier = 0.1,
            AuthorityBoost = 1.2,
        };
    }

    private static BrandRankingProfile Clone(BrandRankingProfile p) => new()
    {
        Positioning = p.Positioning,
        AudiencePrimary = p.AudiencePrimary,
        AudienceSecondary = p.AudienceSecondary,
        AuthorityTopics = [.. p.AuthorityTopics],
        AntiTopics = [.. p.AntiTopics],
        VoiceMarkers = [.. p.VoiceMarkers],
        Pillars = [.. p.Pillars.Select(x => new BrandPillar { Id = x.Id, Name = x.Name, Description = x.Description, Weight = x.Weight, Order = x.Order })],
        HalfLifeDays = p.HalfLifeDays,
        DecayFloor = p.DecayFloor,
        AntiTopicMultiplier = p.AntiTopicMultiplier,
        AuthorityBoost = p.AuthorityBoost,
    };

    [Fact]
    public void RequiresVersionBump_NoChange_False()
    {
        var current = Make(out _);
        Assert.False(current.RequiresVersionBump(Clone(current)));
    }

    [Fact]
    public void RequiresVersionBump_WeightChangeOnly_False()
    {
        var current = Make(out _);
        var proposed = Clone(current);
        proposed.Pillars[0].Weight = 0.9;
        Assert.False(current.RequiresVersionBump(proposed));
    }

    [Theory]
    [InlineData("halflife")]
    [InlineData("floor")]
    [InlineData("antimult")]
    [InlineData("authboost")]
    [InlineData("order")]
    public void RequiresVersionBump_QueryTimeKnobChange_False(string field)
    {
        var current = Make(out _);
        var proposed = Clone(current);
        switch (field)
        {
            case "halflife": proposed.HalfLifeDays = 14; break;
            case "floor": proposed.DecayFloor = 0.2; break;
            case "antimult": proposed.AntiTopicMultiplier = 0.05; break;
            case "authboost": proposed.AuthorityBoost = 1.5; break;
            case "order": proposed.Pillars[0].Order = 5; break;
        }
        Assert.False(current.RequiresVersionBump(proposed));
    }

    [Fact]
    public void RequiresVersionBump_PillarNameChange_True()
    {
        var current = Make(out _);
        var proposed = Clone(current);
        proposed.Pillars[0].Name = "Renamed Pillar";
        Assert.True(current.RequiresVersionBump(proposed));
    }

    [Fact]
    public void RequiresVersionBump_PillarDescriptionChange_True()
    {
        var current = Make(out _);
        var proposed = Clone(current);
        proposed.Pillars[0].Description = "totally different description";
        Assert.True(current.RequiresVersionBump(proposed));
    }

    [Fact]
    public void RequiresVersionBump_AddPillar_True()
    {
        var current = Make(out _);
        var proposed = Clone(current);
        proposed.Pillars.Add(new BrandPillar { Id = Guid.NewGuid(), Name = "New", Description = "new", Weight = 0.1, Order = 1 });
        Assert.True(current.RequiresVersionBump(proposed));
    }

    [Fact]
    public void RequiresVersionBump_RemovePillar_True()
    {
        var current = Make(out _);
        var proposed = Clone(current);
        proposed.Pillars.Clear();
        Assert.True(current.RequiresVersionBump(proposed));
    }

    [Theory]
    [InlineData("positioning")]
    [InlineData("audienceprimary")]
    [InlineData("audiencesecondary")]
    [InlineData("authoritytopics")]
    [InlineData("antitopics")]
    [InlineData("voicemarkers")]
    public void RequiresVersionBump_DefinitionFieldChange_True(string field)
    {
        var current = Make(out _);
        var proposed = Clone(current);
        switch (field)
        {
            case "positioning": proposed.Positioning = "new positioning"; break;
            case "audienceprimary": proposed.AudiencePrimary = "new audience"; break;
            case "audiencesecondary": proposed.AudienceSecondary = "new secondary"; break;
            case "authoritytopics": proposed.AuthorityTopics.Add("new topic"); break;
            case "antitopics": proposed.AntiTopics.Add("new anti"); break;
            case "voicemarkers": proposed.VoiceMarkers.Add("new marker"); break;
        }
        Assert.True(current.RequiresVersionBump(proposed));
    }
}
