using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Analytics.Queries;

public static class GetWebsiteAnalytics
{
    public record Query(DateTimeOffset From, DateTimeOffset To) : IRequest<Result<WebsiteAnalyticsDto>>;

    public sealed class Handler(IGoogleAnalyticsService ga) : IRequestHandler<Query, Result<WebsiteAnalyticsDto>>
    {
        public async Task<Result<WebsiteAnalyticsDto>> Handle(Query request, CancellationToken ct)
        {
            var overviewTask = ga.GetOverviewAsync(request.From, request.To, ct);
            var pagesTask = ga.GetTopPagesAsync(request.From, request.To, 10, ct);
            var sourcesTask = ga.GetTrafficSourcesAsync(request.From, request.To, ct);
            var queriesTask = ga.GetTopQueriesAsync(request.From, request.To, 20, ct);

            await Task.WhenAll(overviewTask, pagesTask, sourcesTask, queriesTask);

            var overview = (await overviewTask).Value ?? new WebsiteOverview(0, 0, 0, 0, 0, 0);
            var pages = (await pagesTask).Value ?? [];
            var sources = (await sourcesTask).Value ?? [];
            var queries = (await queriesTask).Value ?? [];

            return Result<WebsiteAnalyticsDto>.Success(
                new WebsiteAnalyticsDto(overview, pages, sources, queries));
        }
    }
}
