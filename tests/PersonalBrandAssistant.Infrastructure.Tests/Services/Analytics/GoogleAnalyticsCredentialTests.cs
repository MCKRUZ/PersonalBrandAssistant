using Google.Apis.Auth.OAuth2;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Analytics;

public class GoogleAnalyticsCredentialTests
{
    [Fact]
    public void ServiceAccountCredentialLoading_ThrowsWithInvalidJsonStructure()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            // A well-formed JSON but with an invalid/fake RSA key will be rejected by the new API
            var fakeServiceAccountJson = """
            {
                "type": "service_account",
                "project_id": "test-project",
                "private_key_id": "key123",
                "private_key": "not-a-real-key",
                "client_email": "test@test-project.iam.gserviceaccount.com",
                "client_id": "123456789"
            }
            """;
            File.WriteAllText(tempPath, fakeServiceAccountJson);

            // Validates that the credential factory rejects invalid keys
            Assert.ThrowsAny<Exception>(() =>
            {
#pragma warning disable CS0618
                GoogleCredential.FromFile(tempPath);
#pragma warning restore CS0618
            });
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void ServiceAccountCredentialLoading_FailsGracefully_WithMissingFile()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing.json");

#pragma warning disable CS0618
        Assert.ThrowsAny<Exception>(() => GoogleCredential.FromFile(nonExistentPath));
#pragma warning restore CS0618
    }
}
