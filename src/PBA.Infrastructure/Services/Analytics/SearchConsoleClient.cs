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
    private readonly SearchConsoleService _service;

    public SearchConsoleClient(IOptions<GoogleAnalyticsOptions> options)
    {
        var credential = CredentialFactory
            .FromFile<ServiceAccountCredential>(options.Value.CredentialsPath)
            .ToGoogleCredential()
            .CreateScoped(SearchConsoleService.Scope.WebmastersReadonly);

        _service = new SearchConsoleService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "PersonalBrandAssistant"
        });
    }

    public async Task<SearchAnalyticsQueryResponse> QueryAsync(
        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct) =>
        await _service.Searchanalytics.Query(request, siteUrl).ExecuteAsync(ct);
}
