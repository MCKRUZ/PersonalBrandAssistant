using Google.Analytics.Data.V1Beta;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Analytics;

public sealed class Ga4Client : IGa4Client
{
    private readonly Lazy<BetaAnalyticsDataClient> _client;

    public Ga4Client(IOptions<GoogleAnalyticsOptions> options)
    {
        var path = options.Value.CredentialsPath;
        _client = new Lazy<BetaAnalyticsDataClient>(() =>
        {
            var credential = CredentialFactory
                .FromFile<ServiceAccountCredential>(path)
                .ToGoogleCredential();

            return new BetaAnalyticsDataClientBuilder
            {
                GoogleCredential = credential
            }.Build();
        });
    }

    public Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct) =>
        _client.Value.RunReportAsync(request, ct);
}
