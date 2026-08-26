using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Media;
using Xunit;

namespace PBA.Infrastructure.Tests.Media;

public class R2MediaHostTests
{
    private readonly Mock<IAmazonS3> _s3 = new();

    private readonly R2Options _options = new()
    {
        Enabled = true,
        AccessKeyId = "test-access",
        SecretAccessKey = "test-secret",
        Endpoint = "https://122c54305bb75c93b4ad2eeb5e616d79.r2.cloudflarestorage.com",
        PublicBaseUrl = "https://media.matthewkruczek.ai",
        Bucket = "pba-media",
        KeyPrefix = "tiktok/"
    };

    private R2MediaHost CreateHost()
    {
        var monitor = new Mock<IOptionsMonitor<R2Options>>();
        monitor.Setup(o => o.CurrentValue).Returns(_options);
        return new R2MediaHost(new Lazy<IAmazonS3>(() => _s3.Object), monitor.Object);
    }

    [Fact]
    public async Task UploadAsync_PutsUnderPrefixAndReturnsPublicCustomDomainUrl()
    {
        PutObjectRequest? putRequest = null;

        _s3.Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((req, _) => putRequest = req)
            .ReturnsAsync(new PutObjectResponse());

        var host = CreateHost();
        var result = await host.UploadAsync(
            new byte[8], "clip.mp4", "video/mp4", CancellationToken.None);

        Assert.NotNull(putRequest);
        Assert.Equal("pba-media", putRequest!.BucketName);
        Assert.StartsWith("tiktok/", putRequest.Key);
        Assert.EndsWith(".mp4", putRequest.Key);
        Assert.Equal("video/mp4", putRequest.ContentType);

        // The URL is the public custom domain + the object key — unsigned, so Buffer's HEAD probe
        // passes. No presign is issued.
        Assert.Equal($"https://media.matthewkruczek.ai/{putRequest.Key}", result.Url);
        Assert.Equal(putRequest.Key, result.Key);
        _s3.Verify(s => s.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_CallsDeleteObjectWithKey()
    {
        DeleteObjectRequest? deleteRequest = null;
        _s3.Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((req, _) => deleteRequest = req)
            .ReturnsAsync(new DeleteObjectResponse());

        var host = CreateHost();
        await host.DeleteAsync("tiktok/abc.mp4", CancellationToken.None);

        Assert.NotNull(deleteRequest);
        Assert.Equal("pba-media", deleteRequest!.BucketName);
        Assert.Equal("tiktok/abc.mp4", deleteRequest.Key);
    }
}
