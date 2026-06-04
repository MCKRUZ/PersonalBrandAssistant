using Google.Analytics.Data.V1Beta;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Analytics;

public sealed class Ga4Client : IGa4Client
{
    private readonly BetaAnalyticsDataClient _client;

    public Ga4Client(IOptions<GoogleAnalyticsOptions> options)
    {
        var credential = CredentialFactory
            .FromFile<ServiceAccountCredential>(options.Value.CredentialsPath)
            .ToGoogleCredential();

        _client = new BetaAnalyticsDataClientBuilder
        {
            GoogleCredential = credential
        }.Build();
    }

    public Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct) =>
        _client.RunReportAsync(request, ct);
}
