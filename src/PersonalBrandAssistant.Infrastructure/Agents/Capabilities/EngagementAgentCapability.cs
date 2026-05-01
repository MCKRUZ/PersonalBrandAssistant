using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

public sealed class EngagementAgentCapability : AgentCapabilityBase
{
    public EngagementAgentCapability(ISkillRegistry skillRegistry, ILogger<EngagementAgentCapability> logger)
        : base(skillRegistry, logger) { }

    public override AgentCapabilityType Type => AgentCapabilityType.Engagement;
    public override ModelTier DefaultModelTier => ModelTier.Fast;
    protected override string AgentName => "engagement";
    protected override string SkillName => "engagement";
    protected override string DefaultTemplate => "response-suggestion";
    protected override bool CreatesContent => false;
}
