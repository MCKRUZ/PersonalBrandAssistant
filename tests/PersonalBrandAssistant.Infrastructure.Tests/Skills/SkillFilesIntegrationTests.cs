using Microsoft.Extensions.Logging.Abstractions;
using PersonalBrandAssistant.Application.Common.Models.Skills;
using PersonalBrandAssistant.Infrastructure.Skills;

namespace PersonalBrandAssistant.Infrastructure.Tests.Skills;

/// <summary>
/// Verifies that the five SKILL.md files on disk are parseable and meet invariants.
/// Tests use AppContext.BaseDirectory so the files must be copied to the output directory.
/// </summary>
public class SkillFilesIntegrationTests
{
    private static (SkillDefinition Definition, string Level2Body) ParseSkillFile(string skillName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "skills", skillName, "SKILL.md");
        var content = File.ReadAllText(path);
        var result = SkillMetadataParser.Parse(content, path, NullLogger.Instance);

        Assert.NotNull(result);
        return result!.Value;
    }

    [Fact]
    public void Parse_WriterSkillMd_ReturnsValidDefinition()
    {
        var (definition, level2Body) = ParseSkillFile("writer");

        Assert.Equal("writer", definition.Id);
        Assert.Equal("Writer", definition.Name);
        Assert.Equal(1, definition.SchemaVersion);
        Assert.Equal("claude-opus-4-6", definition.ModelId);
        Assert.Empty(definition.AllowedTools);
        Assert.Contains("{{ brand_voice_block }}", level2Body);
    }

    [Fact]
    public void Parse_SocialSkillMd_ReturnsValidDefinition()
    {
        var (definition, level2Body) = ParseSkillFile("social");

        Assert.Equal("social", definition.Id);
        Assert.Equal("Social", definition.Name);
        Assert.Equal(1, definition.SchemaVersion);
        Assert.Null(definition.ModelId);
        Assert.Empty(definition.AllowedTools);
        Assert.Contains("{{ brand_voice_block }}", level2Body);
    }

    [Fact]
    public void Parse_RepurposeSkillMd_ReturnsValidDefinition()
    {
        var (definition, level2Body) = ParseSkillFile("repurpose");

        Assert.Equal("repurpose", definition.Id);
        Assert.Equal("Repurpose", definition.Name);
        Assert.Equal(1, definition.SchemaVersion);
        Assert.Null(definition.ModelId);
        Assert.Empty(definition.AllowedTools);
        Assert.Contains("{{ brand_voice_block }}", level2Body);
    }

    [Fact]
    public void Parse_EngagementSkillMd_ReturnsValidDefinition()
    {
        var (definition, level2Body) = ParseSkillFile("engagement");

        Assert.Equal("engagement", definition.Id);
        Assert.Equal("Engagement", definition.Name);
        Assert.Equal(1, definition.SchemaVersion);
        Assert.Null(definition.ModelId);
        Assert.Empty(definition.AllowedTools);
        Assert.Contains("{{ brand_voice_block }}", level2Body);
    }

    [Fact]
    public void Parse_AnalyticsSkillMd_ReturnsValidDefinition()
    {
        var (definition, level2Body) = ParseSkillFile("analytics");

        Assert.Equal("analytics", definition.Id);
        Assert.Equal("Analytics", definition.Name);
        Assert.Equal(1, definition.SchemaVersion);
        Assert.Null(definition.ModelId);
        Assert.Empty(definition.AllowedTools);
        Assert.Contains("{{ brand_voice_block }}", level2Body);
    }
}
