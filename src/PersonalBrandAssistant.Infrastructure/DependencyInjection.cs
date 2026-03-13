using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<AuditableInterceptor>();
        services.AddScoped<AuditLogInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(
                sp.GetRequiredService<AuditableInterceptor>(),
                sp.GetRequiredService<AuditLogInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(
                configuration["DataProtection:KeyPath"] ?? "data-protection-keys"))
            .SetApplicationName("PersonalBrandAssistant");

        services.AddSingleton<IEncryptionService, EncryptionService>();

        services.AddScoped<IWorkflowEngine, WorkflowEngine>();

        services.AddHostedService<DataSeeder>();
        services.AddHostedService<AuditLogCleanupService>();

        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>();

        return services;
    }
}
