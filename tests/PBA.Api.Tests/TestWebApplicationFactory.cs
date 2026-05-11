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

            services.AddSingleton(new Mock<IFreshRssClient>().Object);
            services.AddSingleton(new Mock<IContentScheduler>().Object);
        });

        builder.UseEnvironment("Testing");
    }
}
