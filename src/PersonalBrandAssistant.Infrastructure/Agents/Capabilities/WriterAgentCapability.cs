using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

public sealed partial class WriterAgentCapability : AgentCapabilityBase
{
    public WriterAgentCapability(ILogger<WriterAgentCapability> logger) : base(logger) { }

    public override AgentCapabilityType Type => AgentCapabilityType.Writer;
    public override ModelTier DefaultModelTier => ModelTier.Standard;
    protected override string AgentName => "writer";
    protected override string DefaultTemplate => "blog-post";
    protected override bool CreatesContent => true;

    protected override Result<AgentOutput> BuildOutput(string responseText, UsageDetails? usage)
    {
        var title = ExtractTitle(responseText);

        return Result<AgentOutput>.Success(new AgentOutput
        {
            GeneratedText = responseText,
            Title = title,
            CreatesContent = true,
            InputTokens = (int)(usage?.InputTokenCount ?? 0),
            OutputTokens = (int)(usage?.OutputTokenCount ?? 0)
        });
    }

    private static string? ExtractTitle(string text)
    {
        var match = TitlePattern().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex TitlePattern();
}
