namespace PersonalBrandAssistant.Application.Common.Models;

public record BrandVoiceScore(
    int OverallScore,
    int ToneAlignment,
    int VocabularyConsistency,
    int PersonaFidelity,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> RuleViolations);
