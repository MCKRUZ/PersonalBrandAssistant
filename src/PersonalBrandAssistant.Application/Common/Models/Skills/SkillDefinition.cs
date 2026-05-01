namespace PersonalBrandAssistant.Application.Common.Models.Skills;

public record SkillDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required string SkillType { get; init; }
    public string? ModelId { get; init; }
    public required IReadOnlyList<string> AllowedTools { get; init; }
    public int SchemaVersion { get; init; }
}
