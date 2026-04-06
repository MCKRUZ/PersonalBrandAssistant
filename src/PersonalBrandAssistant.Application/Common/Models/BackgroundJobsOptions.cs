namespace PersonalBrandAssistant.Application.Common.Models;

public class BackgroundJobsOptions
{
    public const string SectionName = "BackgroundJobs";

    public bool RepurposeOnPublishEnabled { get; set; } = true;
    public bool TrendAggregationEnabled { get; set; } = true;
    public bool CalendarSlotProcessingEnabled { get; set; } = true;
    public bool EngagementAggregationEnabled { get; set; } = true;
    public bool EngagementSchedulerEnabled { get; set; } = true;
}
