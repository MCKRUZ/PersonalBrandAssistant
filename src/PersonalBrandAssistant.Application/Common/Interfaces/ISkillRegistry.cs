using PersonalBrandAssistant.Application.Common.Models.Skills;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ISkillRegistry
{
    SkillDefinition? GetSkillById(string id);
    IReadOnlyCollection<SkillDefinition> GetAllSkills();
    string LoadLevel2(string skillId);
}
