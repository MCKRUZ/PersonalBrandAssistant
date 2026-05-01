namespace PersonalBrandAssistant.Application.Common.Models;

public record BrandVoiceScore(
    int OverallScore,
    int Authoritative,
    int Pragmatic,
    int Concise,
    int Practitioner,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> RuleViolations)
{
    public static BrandVoiceScore Create(
        int authoritative, int pragmatic, int concise, int practitioner,
        IReadOnlyList<string> issues, IReadOnlyList<string> ruleViolations)
    {
        authoritative = Math.Clamp(authoritative, 0, 100);
        pragmatic = Math.Clamp(pragmatic, 0, 100);
        concise = Math.Clamp(concise, 0, 100);
        practitioner = Math.Clamp(practitioner, 0, 100);
        var overall = (authoritative + pragmatic + concise + practitioner) / 4;
        return new BrandVoiceScore(overall, authoritative, pragmatic, concise, practitioner, issues, ruleViolations);
    }
}
