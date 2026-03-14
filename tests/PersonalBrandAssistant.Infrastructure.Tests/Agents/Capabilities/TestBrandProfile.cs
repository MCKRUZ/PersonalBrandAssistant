using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;

internal static class TestBrandProfile
{
    public static BrandProfilePromptModel Create() =>
        new()
        {
            Name = "Test Brand",
            PersonaDescription = "Test persona",
            ToneDescriptors = ["professional"],
            StyleGuidelines = "Be concise",
            PreferredTerms = ["innovation"],
            AvoidedTerms = ["synergy"],
            Topics = ["tech"],
            ExampleContent = ["Example post"]
        };
}
