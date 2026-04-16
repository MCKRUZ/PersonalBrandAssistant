using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models.Skills;

namespace PersonalBrandAssistant.Infrastructure.Skills;

public sealed class SkillRegistry : ISkillRegistry
{
    private const int MaxDepth = 3;

    private readonly IReadOnlyDictionary<string, SkillCacheEntry> _skills;
    private readonly IReadOnlyCollection<SkillDefinition> _allSkills;
    private readonly ConcurrentDictionary<string, Lazy<string>> _level2Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SkillRegistry> _logger;

    public SkillRegistry(
        IOptions<SkillOptions> options,
        IHostEnvironment environment,
        ILogger<SkillRegistry> logger)
    {
        _logger = logger;
        var opts = options.Value;

        if (!Directory.Exists(opts.SkillsPath))
            throw new DirectoryNotFoundException(
                $"Skills directory not found: '{opts.SkillsPath}'. " +
                "Ensure the skills/ directory is published with the application.");

        _skills = Discover(opts.SkillsPath, logger);
        _allSkills = _skills.Values.Select(e => e.Definition).ToList().AsReadOnly();

        ValidateRequired(opts.RequiredSkillIds, environment, logger);
        LogStartupInfo();
    }

    public SkillDefinition? GetSkillById(string id) =>
        _skills.TryGetValue(id, out var entry) ? entry.Definition : null;

    public IReadOnlyCollection<SkillDefinition> GetAllSkills() => _allSkills;

    /// <summary>
    /// Returns the raw (unrendered) Level 2 Liquid template body.
    /// Loaded from cached content on first call; subsequent calls return the cached value.
    /// Throws KeyNotFoundException if skillId is not in the registry.
    /// </summary>
    public string LoadLevel2(string skillId)
    {
        if (!_skills.TryGetValue(skillId, out var entry))
            throw new KeyNotFoundException($"Skill '{skillId}' not found in registry.");

        return _level2Cache.GetOrAdd(
            skillId,
            _ => new Lazy<string>(
                () => ExtractLevel2Body(entry.RawContent, entry.FilePath),
                LazyThreadSafetyMode.ExecutionAndPublication)
        ).Value;
    }

    private static string ExtractLevel2Body(string rawContent, string filePath)
    {
        var parsed = SkillMetadataParser.Parse(
            rawContent, filePath, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        return parsed?.Level2Body ?? "";
    }

    private static IReadOnlyDictionary<string, SkillCacheEntry> Discover(
        string skillsPath, ILogger logger)
    {
        var result = new Dictionary<string, SkillCacheEntry>(StringComparer.OrdinalIgnoreCase);

        var skillFiles = Directory
            .EnumerateFiles(skillsPath, "SKILL.md", SearchOption.AllDirectories)
            .Where(f => ComputeRelativeDepth(skillsPath, f) <= MaxDepth);

        foreach (var filePath in skillFiles)
        {
            var rawContent = File.ReadAllText(filePath);
            var parsed = SkillMetadataParser.Parse(rawContent, filePath, logger);
            if (parsed is null)
                continue;

            var sha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(rawContent))).ToLowerInvariant();

            var (definition, _) = parsed.Value;
            result[definition.Id] = new SkillCacheEntry(definition, filePath, rawContent, sha256);
        }

        return result;
    }

    private static int ComputeRelativeDepth(string basePath, string filePath)
    {
        var relPath = Path.GetRelativePath(basePath, filePath);
        var parts = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length - 1; // subtract SKILL.md filename itself
    }

    private void ValidateRequired(
        IReadOnlyList<string> requiredIds,
        IHostEnvironment environment,
        ILogger logger)
    {
        if (requiredIds.Count == 0)
            return;

        var missingIds = requiredIds
            .Where(id => !_skills.ContainsKey(id))
            .ToList();

        if (missingIds.Count == 0)
            return;

        if (environment.IsProduction())
            throw new InvalidOperationException(
                $"Required skill(s) missing: {string.Join(", ", missingIds)}. " +
                "Ensure all required SKILL.md files are deployed.");

        logger.LogWarning(
            "Required skill(s) missing in {Environment}: {MissingIds}",
            environment.EnvironmentName,
            missingIds);
    }

    private void LogStartupInfo()
    {
        var ids = string.Join(", ", _skills.Keys.Order());
        _logger.LogInformation("SkillRegistry discovered {Count} skill(s): {SkillIds}", _skills.Count, ids);

        foreach (var (id, entry) in _skills)
            _logger.LogInformation("SHA-256 [{SkillId}]: {Hash}", id, entry.Sha256Hash);
    }
}
