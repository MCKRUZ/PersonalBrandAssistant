namespace PBA.Application.Common.Interfaces;

public interface IContentScheduler
{
    string SchedulePublish(Guid contentId, DateTimeOffset publishAt);
    void CancelScheduledPublish(string jobId);
}
