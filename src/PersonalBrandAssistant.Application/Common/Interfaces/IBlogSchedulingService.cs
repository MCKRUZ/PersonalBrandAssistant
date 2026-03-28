using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IBlogSchedulingService
{
    Task OnSubstackPublicationConfirmedAsync(Guid contentId, DateTimeOffset substackPublishedAt, CancellationToken ct);
    Task<Result<DateTimeOffset>> ConfirmBlogScheduleAsync(Guid contentId, CancellationToken ct);
    Task<Result<bool>> ValidateBlogPublishAllowedAsync(Guid contentId, CancellationToken ct);
}
