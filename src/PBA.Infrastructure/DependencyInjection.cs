using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Publishing;
using PBA.Infrastructure.Services;

namespace PBA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureDependencies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddHttpClient();

        services.Configure<FreshRssOptions>(configuration.GetSection(FreshRssOptions.SectionName));
        services.AddHttpClient<FreshRssClient>();
        services.AddHostedService<RssPollingService>();

        services.Configure<SidecarOptions>(configuration.GetSection(SidecarOptions.SectionName));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ISidecarClient, SidecarClient>();
        services.AddHostedService<AiConnectionsService>();

        services.Configure<BlogConnectorOptions>(configuration.GetSection(BlogConnectorOptions.SectionName));
        services.AddScoped<IBlogConnector, BlogConnector>();

        services.AddScoped<IContentPublisher, ContentPublisher>();
        services.AddScoped<IContentScheduler, HangfireContentScheduler>();
        services.AddHostedService<ScheduledPublishReconciler>();

        return services;
    }
}
