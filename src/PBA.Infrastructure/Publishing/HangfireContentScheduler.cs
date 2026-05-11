using Hangfire;
using PBA.Application.Common.Interfaces;

namespace PBA.Infrastructure.Publishing;

public sealed class HangfireContentScheduler(IBackgroundJobClient client) : IContentScheduler
{
    public string SchedulePublish(Guid contentId, DateTimeOffset publishAt) =>
        client.Schedule<IContentPublisher>(p => p.PublishAsync(contentId), publishAt);

    public void CancelScheduledPublish(string jobId) =>
        client.Delete(jobId);
}
