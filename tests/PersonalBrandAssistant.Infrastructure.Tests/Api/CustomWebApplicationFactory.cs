using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _environment;

    public CustomWebApplicationFactory(string connectionString, string environment = "Development")
    {
        _connectionString = connectionString;
        _environment = environment;
    }

    public const string TestApiKey = "test-api-key-12345";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("ApiKey", TestApiKey);
        builder.UseSetting("AuditLog:RetentionDays", "90");

        builder.ConfigureTestServices(services =>
        {
            // Remove hosted services that depend on DB schema existing at startup
            RemoveService<DataSeeder>(services);
            RemoveService<AuditLogCleanupService>(services);
        });
    }

    private static void RemoveService<T>(IServiceCollection services)
    {
        var descriptors = services.Where(d => d.ImplementationType == typeof(T)).ToList();
        foreach (var descriptor in descriptors)
            services.Remove(descriptor);
    }

    public async Task EnsureDatabaseCreatedAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        return client;
    }
}
