namespace PersonalBrandAssistant.Infrastructure.Skills;

public class SkillOptions
{
    public const string SectionName = "Skills";

    /// <summary>
    /// Root path for SKILL.md discovery.
    /// Defaults to AppContext.BaseDirectory/skills at runtime.
    /// Single-file publish is NOT supported in Phase 1.
    /// </summary>
    public string SkillsPath { get; init; } =
        Path.Combine(AppContext.BaseDirectory, "skills");

    /// <summary>
    /// Skill IDs that must be present at startup.
    /// Production: throws if any are missing.
    /// Development: logs a warning and continues.
    /// </summary>
    public IReadOnlyList<string> RequiredSkillIds { get; init; } =
        ["writer", "social", "repurpose", "engagement", "analytics"];
}
