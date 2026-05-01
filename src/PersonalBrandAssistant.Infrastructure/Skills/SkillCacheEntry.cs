using PersonalBrandAssistant.Application.Common.Models.Skills;

namespace PersonalBrandAssistant.Infrastructure.Skills;

internal record SkillCacheEntry(SkillDefinition Definition, string FilePath, string RawContent, string Sha256Hash);
