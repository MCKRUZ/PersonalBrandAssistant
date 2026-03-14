using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

public sealed class RepurposeAgentCapability : AgentCapabilityBase
{
    public RepurposeAgentCapability(ILogger<RepurposeAgentCapability> logger) : base(logger) { }

    public override AgentCapabilityType Type => AgentCapabilityType.Repurpose;
    public override ModelTier DefaultModelTier => ModelTier.Standard;
    protected override string AgentName => "repurpose";
    protected override string DefaultTemplate => "blog-to-thread";
    protected override bool CreatesContent => true;
}
