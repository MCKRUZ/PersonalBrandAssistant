using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;

public interface ITrendSourcePoller
{
    TrendSourceType SourceType { get; }
    Task<List<TrendItem>> PollAsync(TrendSource source, CancellationToken ct);
}
