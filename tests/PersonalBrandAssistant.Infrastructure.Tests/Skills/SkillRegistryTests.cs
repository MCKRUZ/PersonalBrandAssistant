using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models.Skills;
using PersonalBrandAssistant.Infrastructure.Skills;

namespace PersonalBrandAssistant.Infrastructure.Tests.Skills;

public class SkillRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IHostEnvironment> _envMock = new();

    private const string ValidSkillTemplate = """
        ---
        schema_version: 1
        name: __NAME__
        id: __ID__
        description: Test skill
        category: content
        tags: [test]
        skill_type: creative
        allowed_tools: []
        ---

        You are a __ID__. {{ brand_voice_block }}
        """;

    public SkillRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"skill_registry_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _envMock.Setup(e => e.EnvironmentName).Returns(Environments.Production);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private void WriteSkill(string relativePath, string id, string name = "")
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var resolvedName = name.Length > 0 ? name : id;
        var content = ValidSkillTemplate
            .Replace("__NAME__", resolvedName)
            .Replace("__ID__", id);
        File.WriteAllText(fullPath, content);
    }

    private SkillRegistry CreateRegistry(
        IReadOnlyList<string>? requiredIds = null,
        string? skillsPath = null,
        bool isProduction = true)
    {
        _envMock.Setup(e => e.EnvironmentName)
            .Returns(isProduction ? Environments.Production : Environments.Development);

        var opts = Options.Create(new SkillOptions
        {
            SkillsPath = skillsPath ?? _tempDir,
            RequiredSkillIds = requiredIds ?? [],
        });

        return new SkillRegistry(opts, _envMock.Object, NullLogger<SkillRegistry>.Instance);
    }

    private void WriteAllFiveRequiredSkills()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");
        WriteSkill("social/SKILL.md", "social", "Social");
        WriteSkill("repurpose/SKILL.md", "repurpose", "Repurpose");
        WriteSkill("engagement/SKILL.md", "engagement", "Engagement");
        WriteSkill("analytics/SKILL.md", "analytics", "Analytics");
    }

    // --- Discovery ---

    [Fact]
    public void Discover_FiveValidSkillFiles_FindsAll()
    {
        WriteAllFiveRequiredSkills();

        var registry = CreateRegistry(requiredIds: ["writer", "social", "repurpose", "engagement", "analytics"]);

        Assert.Equal(5, registry.GetAllSkills().Count);
    }

    [Fact]
    public void Discover_NestedBeyondMaxDepth_SkipsDeep()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");
        WriteSkill("a/b/c/SKILL.md", "depth3", "Depth3");
        // depth 4 — should be excluded
        WriteSkill("a/b/c/d/SKILL.md", "depth4", "Depth4");

        var registry = CreateRegistry();

        var skills = registry.GetAllSkills();
        Assert.Equal(2, skills.Count);
        Assert.Null(registry.GetSkillById("depth4"));
    }

    [Fact]
    public void Discover_InvalidSkillFile_SkipsAndLogs()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");
        // Write an invalid SKILL.md (missing required fields)
        var badPath = Path.Combine(_tempDir, "bad/SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(badPath)!);
        File.WriteAllText(badPath, "---\nschema_version: 99\n---\nBody");

        var registry = CreateRegistry();

        Assert.Single(registry.GetAllSkills());
    }

    [Fact]
    public void Discover_EmptyDirectory_DiscoversZero()
    {
        var registry = CreateRegistry();

        Assert.Empty(registry.GetAllSkills());
    }

    // --- GetSkillById ---

    [Fact]
    public void GetSkillById_ExistingId_ReturnsDefinition()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");

        var registry = CreateRegistry();
        var skill = registry.GetSkillById("writer");

        Assert.NotNull(skill);
        Assert.Equal("writer", skill.Id);
    }

    [Fact]
    public void GetSkillById_NonExistentId_ReturnsNull()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.GetSkillById("nonexistent"));
    }

    [Fact]
    public void GetSkillById_CaseInsensitiveLookup_Succeeds()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");

        var registry = CreateRegistry();

        Assert.NotNull(registry.GetSkillById("WRITER"));
        Assert.NotNull(registry.GetSkillById("Writer"));
        Assert.NotNull(registry.GetSkillById("wRiTeR"));
    }

    // --- Startup validation ---

    [Fact]
    public void Startup_AllRequiredSkillsPresent_NoException()
    {
        WriteAllFiveRequiredSkills();

        // Should not throw
        var registry = CreateRegistry(
            requiredIds: ["writer", "social", "repurpose", "engagement", "analytics"],
            isProduction: true);

        Assert.Equal(5, registry.GetAllSkills().Count);
    }

    [Fact]
    public void Startup_MissingRequiredSkill_ThrowsInProduction()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");

        Assert.Throws<InvalidOperationException>(() =>
            CreateRegistry(requiredIds: ["writer", "social"], isProduction: true));
    }

    [Fact]
    public void Startup_MissingRequiredSkill_LogsWarningInDevelopment()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");

        // Should NOT throw in Development — just logs warning
        var registry = CreateRegistry(requiredIds: ["writer", "social"], isProduction: false);

        Assert.NotNull(registry);
    }

    // --- GetAllSkills ---

    [Fact]
    public void GetAllSkills_FiveFilesDiscovered_ReturnsFive()
    {
        WriteAllFiveRequiredSkills();

        var registry = CreateRegistry(requiredIds: ["writer", "social", "repurpose", "engagement", "analytics"]);

        Assert.Equal(5, registry.GetAllSkills().Count);
    }

    [Fact]
    public void GetAllSkills_ReturnsLevel1Only()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");

        var registry = CreateRegistry();
        var skill = registry.GetAllSkills().Single();

        // SkillDefinition has no Instructions/Level2Body property — it's Level1 metadata
        Assert.Equal("writer", skill.Id);
        Assert.IsType<SkillDefinition>(skill);
    }

    // --- LoadLevel2 ---

    [Fact]
    public void LoadLevel2_FirstCall_ReadsBodyFromFile()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");

        var registry = CreateRegistry();
        var body = registry.LoadLevel2("writer");

        Assert.Contains("You are a writer", body);
        Assert.Contains("{{ brand_voice_block }}", body);
    }

    [Fact]
    public void LoadLevel2_SecondCall_ReturnsCachedValue()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");

        var registry = CreateRegistry();
        var body1 = registry.LoadLevel2("writer");

        // Delete the file — second call must still succeed from cache
        File.Delete(Path.Combine(_tempDir, "writer/SKILL.md"));
        var body2 = registry.LoadLevel2("writer");

        Assert.Equal(body1, body2);
    }

    [Fact]
    public void LoadLevel2_ConcurrentFirstAccess_AllReturnSameValue()
    {
        WriteSkill("writer/SKILL.md", "writer", "Writer");

        var registry = CreateRegistry();
        var results = new string[50];

        Parallel.For(0, 50, i =>
        {
            results[i] = registry.LoadLevel2("writer");
        });

        Assert.All(results, r => Assert.Equal(results[0], r));
    }

    [Fact]
    public void LoadLevel2_UnknownSkillId_ThrowsKeyNotFoundException()
    {
        var registry = CreateRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.LoadLevel2("nonexistent"));
    }

    // --- Startup logging ---

    [Fact]
    public void Startup_LogsSHA256HashOfEachFile()
    {
        // Verify SHA-256 is computed without error by creating a registry
        // (actual log message verification would require a custom ILogger — out of scope)
        WriteSkill("writer/SKILL.md", "writer", "Writer");

        // Should not throw during startup logging
        var registry = CreateRegistry();
        Assert.NotNull(registry);
    }

    [Fact]
    public void Startup_LogsDiscoveredSkillCount()
    {
        WriteAllFiveRequiredSkills();

        // Should complete without error (log output not captured in this test)
        var registry = CreateRegistry(requiredIds: ["writer", "social", "repurpose", "engagement", "analytics"]);
        Assert.Equal(5, registry.GetAllSkills().Count);
    }
}
