using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Domain.Entities;

public class BrandProfile : AuditableEntityBase
{
    public string Name { get; set; } = string.Empty;
    public List<string> ToneDescriptors { get; set; } = [];
    public string StyleGuidelines { get; set; } = string.Empty;
    public VocabularyConfig VocabularyPreferences { get; set; } = new();
    public List<string> Topics { get; set; } = [];
    public string PersonaDescription { get; set; } = string.Empty;
    public List<string> ExampleContent { get; set; } = [];
    public bool IsActive { get; set; }
    public uint Version { get; set; }
}
