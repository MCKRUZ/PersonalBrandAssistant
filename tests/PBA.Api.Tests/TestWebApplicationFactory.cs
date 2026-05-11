using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            // Remove all EF Core and NpgsQL service registrations
            var efDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(ApplicationDbContext) ||
                    d.ServiceType == typeof(IAppDbContext) ||
                    d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                    d.ImplementationType?.Assembly.FullName?.Contains("Npgsql") == true)
                .ToList();

            foreach (var d in efDescriptors)
                services.Remove(d);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            services.AddScoped<IAppDbContext>(sp =>
                sp.GetRequiredService<ApplicationDbContext>());

            services.AddSingleton(new Mock<IFreshRssClient>().Object);
        });

        builder.UseEnvironment("Testing");
    }
}
