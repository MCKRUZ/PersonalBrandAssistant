using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

public sealed class AnalyticsAgentCapability : AgentCapabilityBase
{
    public AnalyticsAgentCapability(ILogger<AnalyticsAgentCapability> logger) : base(logger) { }

    public override AgentCapabilityType Type => AgentCapabilityType.Analytics;
    public override ModelTier DefaultModelTier => ModelTier.Fast;
    protected override string AgentName => "analytics";
    protected override string DefaultTemplate => "performance-insights";
    protected override bool CreatesContent => false;
}
