using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IContentScheduler
{
    Task<Result<MediatR.Unit>> ScheduleAsync(Guid contentId, DateTimeOffset scheduledAt, CancellationToken ct = default);
    Task<Result<MediatR.Unit>> RescheduleAsync(Guid contentId, DateTimeOffset newScheduledAt, CancellationToken ct = default);
    Task<Result<MediatR.Unit>> CancelAsync(Guid contentId, CancellationToken ct = default);
}
