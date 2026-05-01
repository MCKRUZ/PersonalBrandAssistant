using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

public sealed class SocialAgentCapability : AgentCapabilityBase
{
    public SocialAgentCapability(ISkillRegistry skillRegistry, ILogger<SocialAgentCapability> logger)
        : base(skillRegistry, logger) { }

    public override AgentCapabilityType Type => AgentCapabilityType.Social;
    public override ModelTier DefaultModelTier => ModelTier.Fast;
    protected override string AgentName => "social";
    protected override string SkillName => "social";
    protected override string DefaultTemplate => "post";
    protected override bool CreatesContent => true;
}
