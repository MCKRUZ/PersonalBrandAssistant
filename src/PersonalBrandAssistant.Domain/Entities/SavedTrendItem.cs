using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Entities;

public class SavedTrendItem : EntityBase
{
    public Guid TrendItemId { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }

    public TrendItem? TrendItem { get; set; }
}
