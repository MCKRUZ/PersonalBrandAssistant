using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Analytics.Queries;

public static class GetAnalyticsHealth
{
    public record Query : IRequest<Result<AnalyticsHealthDto>>;

    public sealed class Handler(IGoogleAnalyticsService ga) : IRequestHandler<Query, Result<AnalyticsHealthDto>>
    {
        public async Task<Result<AnalyticsHealthDto>> Handle(Query request, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;
            var from = now.AddDays(-1);

            var ga4Ok = (await ga.GetOverviewAsync(from, now, ct)).IsSuccess;
            var gscOk = (await ga.GetTopQueriesAsync(from, now, 1, ct)).IsSuccess;

            return Result<AnalyticsHealthDto>.Success(new AnalyticsHealthDto(ga4Ok, gscOk));
        }
    }
}
