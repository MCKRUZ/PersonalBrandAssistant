using Google.Apis.SearchConsole.v1.Data;
using Microsoft.Extensions.Options;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Analytics;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Analytics;

public class SearchConsoleClientTests
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
        var ex = Record.Exception(() => new SearchConsoleClient(MissingCredentialsOptions()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task QueryAsync_MissingCredentialsFile_ThrowsOnFirstCall()
    {
        var sut = new SearchConsoleClient(MissingCredentialsOptions());

        await Assert.ThrowsAnyAsync<Exception>(
            () => sut.QueryAsync("https://example.com/", new SearchAnalyticsQueryRequest(), CancellationToken.None));
    }
}
