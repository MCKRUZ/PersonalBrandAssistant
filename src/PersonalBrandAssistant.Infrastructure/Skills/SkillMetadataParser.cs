using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Models.Skills;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PersonalBrandAssistant.Infrastructure.Skills;

internal record SkillFrontMatter
{
    public int SchemaVersion { get; init; }
    public string Name { get; init; } = "";
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public string SkillType { get; init; } = "";
    public string? ModelId { get; init; }
    public List<string> AllowedTools { get; init; } = [];
}

/// <summary>
/// Parses a SKILL.md file. Returns null if the file is invalid or fails validation.
/// filePath is used only for logging — not stored in the returned SkillDefinition.
/// </summary>
internal static class SkillMetadataParser
{
    private const int SupportedSchemaVersion = 1;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static (SkillDefinition Definition, string Level2Body)? Parse(
        string content, string filePath, ILogger logger)
    {
        // 1. Strip UTF-8 BOM
        if (content.StartsWith('\uFEFF'))
            content = content[1..];

        // 2. Normalize line endings
        content = content.Replace("\r\n", "\n");

        // 3. Must start with ---\n
        if (!content.StartsWith("---\n", StringComparison.Ordinal))
        {
            logger.LogWarning("SKILL.md at '{FilePath}' does not start with frontmatter delimiter", filePath);
            return null;
        }

        // 4. Find closing delimiter (first \n---\n or \n--- at end)
        var closingIndex = content.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        int bodyStart;
        if (closingIndex >= 0)
        {
            bodyStart = closingIndex + 5; // skip \n---\n
        }
        else if (content.EndsWith("\n---", StringComparison.Ordinal))
        {
            closingIndex = content.Length - 4;
            bodyStart = content.Length;
        }
        else
        {
            logger.LogWarning("SKILL.md at '{FilePath}' has no closing frontmatter delimiter", filePath);
            return null;
        }

        // 5. Extract YAML block (between opening --- and closing ---)
        var yamlBlock = content[4..closingIndex]; // skip opening "---\n"

        // 6. Empty YAML block
        if (string.IsNullOrWhiteSpace(yamlBlock))
        {
            logger.LogWarning("SKILL.md at '{FilePath}' has empty frontmatter", filePath);
            return null;
        }

        // 7. Deserialize
        SkillFrontMatter frontMatter;
        try
        {
            frontMatter = Deserializer.Deserialize<SkillFrontMatter>(yamlBlock);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SKILL.md at '{FilePath}' failed YAML deserialization", filePath);
            return null;
        }

        // 8. Validate
        if (frontMatter.SchemaVersion != SupportedSchemaVersion)
        {
            logger.LogWarning(
                "SKILL.md at '{FilePath}' has unsupported schema_version {Version}",
                filePath, frontMatter.SchemaVersion);
            return null;
        }

        if (string.IsNullOrWhiteSpace(frontMatter.Id))
        {
            logger.LogWarning("SKILL.md at '{FilePath}' is missing required field 'id'", filePath);
            return null;
        }

        if (string.IsNullOrWhiteSpace(frontMatter.Name))
        {
            logger.LogWarning("SKILL.md at '{FilePath}' is missing required field 'name'", filePath);
            return null;
        }

        // 9. Normalize Id
        var normalizedId = frontMatter.Id.Trim().ToLowerInvariant();

        // 10. Default lists if null, then freeze to prevent caller mutation
        IReadOnlyList<string> tags = (frontMatter.Tags ?? []).AsReadOnly();
        IReadOnlyList<string> allowedTools = (frontMatter.AllowedTools ?? []).AsReadOnly();

        // 11. Extract Level 2 body
        var level2Body = bodyStart < content.Length
            ? content[bodyStart..].Trim()
            : "";

        // 12. Build result
        var definition = new SkillDefinition
        {
            Id = normalizedId,
            Name = frontMatter.Name,
            Description = frontMatter.Description,
            Category = frontMatter.Category,
            Tags = tags,
            SkillType = frontMatter.SkillType,
            ModelId = frontMatter.ModelId,
            AllowedTools = allowedTools,
            SchemaVersion = frontMatter.SchemaVersion,
        };

        return (definition, level2Body);
    }
}
