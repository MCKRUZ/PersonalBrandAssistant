using Google.Analytics.Data.V1Beta;
using Microsoft.Extensions.Options;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Analytics;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Analytics;

public class Ga4ClientTests
{
    private static IOptions<GoogleAnalyticsOptions> MissingCredentialsOptions() =>
        Options.Create(new GoogleAnalyticsOptions
        {
            PropertyId = "test-property-123",
            SiteUrl = "https://example.com/",
            CredentialsPath = "secrets/does-not-exist.json"
        });

    [Fact]
    public void Ctor_MissingCredentialsFile_DoesNotThrow()
    {
        var ex = Record.Exception(() => new Ga4Client(MissingCredentialsOptions()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task RunReportAsync_MissingCredentialsFile_ThrowsOnFirstCall()
    {
        var sut = new Ga4Client(MissingCredentialsOptions());

        await Assert.ThrowsAnyAsync<Exception>(
            () => sut.RunReportAsync(new RunReportRequest(), CancellationToken.None));
    }
}
