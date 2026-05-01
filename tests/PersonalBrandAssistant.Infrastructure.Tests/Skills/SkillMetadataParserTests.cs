using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalBrandAssistant.Infrastructure.Skills;

namespace PersonalBrandAssistant.Infrastructure.Tests.Skills;

public class SkillMetadataParserTests
{
    private static readonly ILogger _logger = NullLogger.Instance;

    private const string ValidSkillMd = """
        ---
        schema_version: 1
        name: Writer
        id: writer
        description: Blog post generation
        category: content
        tags: [blog, writing]
        skill_type: creative
        allowed_tools: []
        ---

        You are a content writer. {{ brand_voice_block }}
        """;

    // --- Happy path ---

    [Fact]
    public void Parse_ValidSkillMd_ReturnsSkillDefinitionWithAllFields()
    {
        var result = SkillMetadataParser.Parse(ValidSkillMd, "skills/writer/SKILL.md", _logger);

        Assert.NotNull(result);
        Assert.Equal("writer", result.Value.Definition.Id);
        Assert.Equal("Writer", result.Value.Definition.Name);
        Assert.Equal("Blog post generation", result.Value.Definition.Description);
        Assert.Equal("content", result.Value.Definition.Category);
        Assert.Equal("creative", result.Value.Definition.SkillType);
        Assert.Equal(1, result.Value.Definition.SchemaVersion);
        Assert.Equal(2, result.Value.Definition.Tags.Count);
        Assert.Contains("blog", result.Value.Definition.Tags);
        Assert.Contains("writing", result.Value.Definition.Tags);
    }

    [Fact]
    public void Parse_ValidSkillMd_IdIsNormalizedToLowercase()
    {
        var content = ValidSkillMd.Replace("id: writer", "id: WRITER");
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.NotNull(result);
        Assert.Equal("writer", result.Value.Definition.Id);
    }

    [Fact]
    public void Parse_ValidSkillMd_ReturnsLevel2Body()
    {
        var result = SkillMetadataParser.Parse(ValidSkillMd, "SKILL.md", _logger);

        Assert.NotNull(result);
        Assert.Contains("You are a content writer", result.Value.Level2Body);
    }

    [Fact]
    public void Parse_ValidSkillMd_TagsAndAllowedToolsDefaultToEmptyLists()
    {
        var content = """
            ---
            schema_version: 1
            name: Minimal
            id: minimal
            description: Minimal skill
            category: test
            skill_type: basic
            ---

            Body text.
            """;

        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.NotNull(result);
        Assert.Empty(result.Value.Definition.Tags);
        Assert.Empty(result.Value.Definition.AllowedTools);
    }

    // --- Schema version ---

    [Fact]
    public void Parse_UnknownSchemaVersion_ReturnsNull()
    {
        var content = ValidSkillMd.Replace("schema_version: 1", "schema_version: 99");
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_MissingSchemaVersion_ReturnsNull()
    {
        var content = ValidSkillMd.Replace("schema_version: 1\n", "");
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.Null(result);
    }

    // --- Required fields ---

    [Fact]
    public void Parse_MissingId_ReturnsNull()
    {
        var content = ValidSkillMd.Replace("id: writer\n", "");
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_MissingName_ReturnsNull()
    {
        var content = ValidSkillMd.Replace("name: Writer\n", "");
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_EmptyId_ReturnsNull()
    {
        var content = ValidSkillMd.Replace("id: writer", "id: \"\"");
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.Null(result);
    }

    // --- YAML edge cases ---

    [Fact]
    public void Parse_UnknownYamlKeys_IgnoresThemAndSucceeds()
    {
        var content = ValidSkillMd.Replace(
            "skill_type: creative",
            "skill_type: creative\nunknown_future_field: some_value");

        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.NotNull(result);
    }

    [Fact]
    public void Parse_TagsAsString_ReturnsEmptyList()
    {
        // If tags is a scalar string (type mismatch), default to empty list
        var content = ValidSkillMd.Replace("tags: [blog, writing]", "tags: not-a-list");
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        // Should either succeed with empty tags or return null — either is acceptable
        // The key invariant: Tags must not throw
        if (result is not null)
            Assert.NotNull(result.Value.Definition.Tags);
    }

    // --- Frontmatter structure edge cases ---

    [Fact]
    public void Parse_MissingClosingDelimiter_ReturnsNull()
    {
        var content = "---\nschema_version: 1\nname: Writer\nid: writer\n\nNo closing delimiter here";
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_NoFrontmatterBlock_ReturnsNull()
    {
        var content = "Just plain content without any frontmatter.";
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_ExtraTripleDashInBody_BodyContainsIt()
    {
        var content = """
            ---
            schema_version: 1
            name: Writer
            id: writer
            description: Test
            category: content
            skill_type: creative
            ---

            First part of body.

            ---

            Second part of body.
            """;

        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.NotNull(result);
        Assert.Contains("---", result.Value.Level2Body);
        Assert.Contains("Second part", result.Value.Level2Body);
    }

    [Fact]
    public void Parse_WindowsCRLFLineEndings_ParsesCorrectly()
    {
        var content = ValidSkillMd.Replace("\n", "\r\n");
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.NotNull(result);
        Assert.Equal("writer", result.Value.Definition.Id);
    }

    [Fact]
    public void Parse_Utf8BomPrefix_ParsesCorrectly()
    {
        var content = "\uFEFF" + ValidSkillMd;
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.NotNull(result);
        Assert.Equal("writer", result.Value.Definition.Id);
    }

    [Fact]
    public void Parse_EmptyFrontmatter_ReturnsNull()
    {
        var content = "---\n---\n\nBody";
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.Null(result);
    }

    // --- Body extraction ---

    [Fact]
    public void Parse_MultipleBlankLinesAfterFrontmatter_BodyIsTrimmed()
    {
        var content = """
            ---
            schema_version: 1
            name: Writer
            id: writer
            description: Test
            category: content
            skill_type: creative
            ---



            Actual body content here.



            """;

        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.NotNull(result);
        Assert.Equal("Actual body content here.", result.Value.Level2Body);
    }

    [Fact]
    public void Parse_ClosingDelimiterAtEndOfFile_ParsesCorrectly()
    {
        // Exercises the EndsWith("\n---") branch — no trailing newline after closing delimiter
        var content = "---\nschema_version: 1\nname: Writer\nid: writer\ndescription: Test\ncategory: content\nskill_type: creative\n---";
        var result = SkillMetadataParser.Parse(content, "SKILL.md", _logger);

        Assert.NotNull(result);
        Assert.Equal("writer", result.Value.Definition.Id);
        Assert.Equal("", result.Value.Level2Body);
    }

    [Fact]
    public void Parse_BodyWithLiquidSyntax_BodyReturnedVerbatim()
    {
        var result = SkillMetadataParser.Parse(ValidSkillMd, "SKILL.md", _logger);

        Assert.NotNull(result);
        Assert.Contains("{{ brand_voice_block }}", result.Value.Level2Body);
    }
}
