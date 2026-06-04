using Google.Apis.Auth.OAuth2;
using Google.Apis.SearchConsole.v1;
using Google.Apis.SearchConsole.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Analytics;

public sealed class SearchConsoleClient : ISearchConsoleClient
{
    private readonly Lazy<SearchConsoleService> _service;

    public SearchConsoleClient(IOptions<GoogleAnalyticsOptions> options)
    {
        var path = options.Value.CredentialsPath;
        _service = new Lazy<SearchConsoleService>(() =>
        {
            var credential = CredentialFactory
                .FromFile<ServiceAccountCredential>(path)
                .ToGoogleCredential()
                .CreateScoped(SearchConsoleService.Scope.WebmastersReadonly);

            return new SearchConsoleService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "PersonalBrandAssistant"
            });
        });
    }

    public async Task<SearchAnalyticsQueryResponse> QueryAsync(
        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct) =>
        await _service.Value.Searchanalytics.Query(request, siteUrl).ExecuteAsync(ct);
}
