namespace PersonalBrandAssistant.Domain.ValueObjects;

public class VocabularyConfig
{
    public List<string> PreferredTerms { get; set; } = [];
    public List<string> AvoidTerms { get; set; } = [];
}
