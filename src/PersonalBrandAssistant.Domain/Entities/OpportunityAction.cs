using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class OpportunityAction : AuditableEntityBase
{
    public string PostUrl { get; set; } = "";
    public PlatformType Platform { get; set; }
    public OpportunityStatus Status { get; set; }
}
