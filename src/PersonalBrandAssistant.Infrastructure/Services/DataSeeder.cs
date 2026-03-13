using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;

namespace PersonalBrandAssistant.Infrastructure.Services;

public class DataSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DataSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (!await context.BrandProfiles.AnyAsync(cancellationToken))
        {
            context.BrandProfiles.Add(new BrandProfile
            {
                Name = "Default Profile",
                PersonaDescription = "Default brand persona",
                IsActive = true,
            });
            _logger.LogInformation("Seeded default BrandProfile");
        }

        if (!await context.Platforms.AnyAsync(cancellationToken))
        {
            var platforms = Enum.GetValues<PlatformType>().Select(type => new Platform
            {
                Type = type,
                DisplayName = type.ToString(),
                IsConnected = false,
            });
            context.Platforms.AddRange(platforms);
            _logger.LogInformation("Seeded {Count} Platform records", Enum.GetValues<PlatformType>().Length);
        }

        if (!await context.Users.AnyAsync(cancellationToken))
        {
            context.Users.Add(new User
            {
                Email = _configuration["DefaultUser:Email"] ?? "user@example.com",
                DisplayName = "Default User",
                TimeZoneId = _configuration["DefaultUser:TimeZoneId"] ?? "America/New_York",
            });
            _logger.LogInformation("Seeded default User");
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
