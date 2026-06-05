using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Data;

namespace PBA.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "TestDb_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptorsToRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(ApplicationDbContext) ||
                    d.ServiceType == typeof(IAppDbContext) ||
                    d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                    d.ImplementationType?.Assembly.FullName?.Contains("Npgsql") == true ||
                    d.ServiceType.FullName?.Contains("Hangfire") == true ||
                    d.ImplementationType?.FullName?.Contains("Hangfire") == true ||
                    d.ImplementationFactory?.Method.DeclaringType?.FullName?.Contains("Hangfire") == true ||
                    d.ImplementationType?.FullName?.Contains("ScheduledPublishReconciler") == true)
                .ToList();

            foreach (var d in descriptorsToRemove)
                services.Remove(d);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            services.AddScoped<IAppDbContext>(sp =>
                sp.GetRequiredService<ApplicationDbContext>());

            services.AddSingleton(new Mock<IRssFeedReader>().Object);
            services.AddSingleton(new Mock<IContentScheduler>().Object);

            var sidecarMock = new Mock<ISidecarClient>();
            sidecarMock.Setup(x => x.SendPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("""{"score": 85, "feedback": "Good brand voice alignment"}""");
            services.AddSingleton(sidecarMock.Object);

            services.RemoveAll<IPlatformConnector>();
            var blogConnectorMock = new Mock<IPlatformConnector>();
            blogConnectorMock.Setup(x => x.PublishAsync(It.IsAny<PBA.Application.Common.Models.PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PBA.Application.Common.Models.PlatformPublishResult(true, "https://blog.test/published-post", "published-post", null));
            services.AddKeyedSingleton<IPlatformConnector>(PBA.Domain.Enums.Platform.Blog, blogConnectorMock.Object);

            var transformerMock = new Mock<IContentTransformer>();
            transformerMock.Setup(x => x.TransformAsync(It.IsAny<PBA.Domain.Entities.Content>(), It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PBA.Domain.Entities.Content c, PBA.Domain.Enums.Platform _, CancellationToken _) => c.Body);
            services.AddSingleton<IContentTransformer>(transformerMock.Object);

            var oauthMock = new Mock<IOAuthService>();
            oauthMock.Setup(x => x.GetAuthorizationUrlAsync(It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://oauth.test/authorize?state=test");
            oauthMock.Setup(x => x.ExchangeCodeAsync(It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PBA.Domain.Entities.PlatformCredential { Platform = PBA.Domain.Enums.Platform.LinkedIn, IsActive = true, EncryptedAccessToken = "encrypted" });
            services.AddSingleton(oauthMock.Object);

            var encryptorMock = new Mock<ITokenEncryptor>();
            encryptorMock.Setup(x => x.Encrypt(It.IsAny<string>())).Returns<string>(s => $"enc:{s}");
            encryptorMock.Setup(x => x.Decrypt(It.IsAny<string>())).Returns<string>(s => s.Replace("enc:", ""));
            services.AddSingleton(encryptorMock.Object);

            services.AddSingleton(new Mock<IPublishRetryHandler>().Object);
        });

        builder.UseEnvironment("Testing");
    }
}
