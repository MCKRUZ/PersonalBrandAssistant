using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IContentCalendarService
{
    Task<Result<IReadOnlyList<CalendarSlot>>> GetSlotsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<Result<Guid>> CreateSeriesAsync(ContentSeriesRequest request, CancellationToken ct);
    Task<Result<Guid>> CreateManualSlotAsync(CalendarSlotRequest request, CancellationToken ct);
    Task<Result<MediatR.Unit>> AssignContentAsync(Guid slotId, Guid contentId, CancellationToken ct);
    Task<Result<int>> AutoFillSlotsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
