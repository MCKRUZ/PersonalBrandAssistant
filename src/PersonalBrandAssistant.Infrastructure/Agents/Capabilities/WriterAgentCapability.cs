using System.Text.RegularExpressions;
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

    protected override Result<AgentOutput> BuildOutput(
        string responseText, int inputTokens, int outputTokens,
        int cacheReadTokens, int cacheCreationTokens, decimal cost, List<string> fileChanges)
    {
        var baseResult = base.BuildOutput(responseText, inputTokens, outputTokens,
            cacheReadTokens, cacheCreationTokens, cost, fileChanges);

        if (!baseResult.IsSuccess) return baseResult;

        var title = ExtractTitle(responseText);
        return Result<AgentOutput>.Success(baseResult.Value! with { Title = title });
    }

    private static string? ExtractTitle(string text)
    {
        var match = TitlePattern().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex TitlePattern();
}
