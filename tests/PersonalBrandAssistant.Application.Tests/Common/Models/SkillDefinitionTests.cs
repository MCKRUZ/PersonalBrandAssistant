using PersonalBrandAssistant.Application.Common.Models.Skills;

namespace PersonalBrandAssistant.Application.Tests.Common.Models;

public class SkillDefinitionTests
{
    private static SkillDefinition CreateDefault() => new()
    {
        Id = "writer",
        Name = "Writer",
        Description = "Writes content",
        Category = "content",
        Tags = [],
        SkillType = "capability",
        AllowedTools = []
    };

    [Fact]
    public void SkillDefinition_WithExpression_ProducesNewInstance()
    {
        var original = CreateDefault();
        var modified = original with { Name = "Updated Writer" };

        Assert.NotSame(original, modified);
        Assert.Equal("Writer", original.Name);
        Assert.Equal("Updated Writer", modified.Name);
    }

    [Fact]
    public void SkillDefinition_NoFilePath_CannotBeConstructed()
    {
        var property = typeof(SkillDefinition).GetProperty("FilePath");
        Assert.Null(property);
    }

    [Fact]
    public void SkillDefinition_Tags_DefaultsToEmpty()
    {
        var skill = CreateDefault();
        Assert.Empty(skill.Tags);
    }

    [Fact]
    public void SkillDefinition_AllowedTools_DefaultsToEmpty()
    {
        var skill = CreateDefault();
        Assert.Empty(skill.AllowedTools);
    }

    [Fact]
    public void SkillDefinition_ModelId_DefaultsToNull()
    {
        var skill = CreateDefault();
        Assert.Null(skill.ModelId);
    }
}
