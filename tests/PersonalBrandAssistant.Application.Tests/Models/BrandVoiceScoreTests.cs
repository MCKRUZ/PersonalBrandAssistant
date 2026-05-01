using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Tests.Models;

public class BrandVoiceScoreTests
{
    [Fact]
    public void Create_ComputesOverallAsEqualWeightAverage()
    {
        var score = BrandVoiceScore.Create(80, 60, 100, 40, [], []);

        Assert.Equal(70, score.OverallScore);
        Assert.Equal(80, score.Authoritative);
        Assert.Equal(60, score.Pragmatic);
        Assert.Equal(100, score.Concise);
        Assert.Equal(40, score.Practitioner);
    }

    [Fact]
    public void Create_ClampsValuesAbove100()
    {
        var score = BrandVoiceScore.Create(120, 80, 80, 80, [], []);

        Assert.Equal(100, score.Authoritative);
    }

    [Fact]
    public void Create_ClampsValuesBelowZero()
    {
        var score = BrandVoiceScore.Create(-5, 80, 80, 80, [], []);

        Assert.Equal(0, score.Authoritative);
    }

    [Fact]
    public void Create_PreservesIssuesAndRuleViolations()
    {
        var issues = new List<string> { "Too hedging" };
        var violations = new List<string> { "Avoided term: synergy" };

        var score = BrandVoiceScore.Create(80, 80, 80, 80, issues, violations);

        Assert.Single(score.Issues);
        Assert.Single(score.RuleViolations);
        Assert.Equal("Too hedging", score.Issues[0]);
        Assert.Equal("Avoided term: synergy", score.RuleViolations[0]);
    }

    [Fact]
    public void Create_AllZeros_ProducesZeroOverall()
    {
        var score = BrandVoiceScore.Create(0, 0, 0, 0, [], []);

        Assert.Equal(0, score.OverallScore);
    }

    [Fact]
    public void Create_AllHundred_ProducesHundredOverall()
    {
        var score = BrandVoiceScore.Create(100, 100, 100, 100, [], []);

        Assert.Equal(100, score.OverallScore);
    }

    [Fact]
    public void Record_HasValueEquality()
    {
        var a = BrandVoiceScore.Create(80, 60, 100, 40, [], []);
        var b = BrandVoiceScore.Create(80, 60, 100, 40, [], []);

        Assert.Equal(a, b);
    }
}
