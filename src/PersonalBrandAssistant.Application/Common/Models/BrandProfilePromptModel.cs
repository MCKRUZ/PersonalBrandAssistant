namespace PersonalBrandAssistant.Application.Common.Models;

public record BrandProfilePromptModel
{
    public required string Name { get; init; }
    public required string PersonaDescription { get; init; }
    public required IReadOnlyList<string> ToneDescriptors { get; init; }
    public required string StyleGuidelines { get; init; }
    public required IReadOnlyList<string> PreferredTerms { get; init; }
    public required IReadOnlyList<string> AvoidedTerms { get; init; }
    public required IReadOnlyList<string> Topics { get; init; }
    public required IReadOnlyList<string> ExampleContent { get; init; }
}
