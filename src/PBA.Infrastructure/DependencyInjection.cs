using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Publishing;
using PBA.Infrastructure.Seeding;
using PBA.Infrastructure.Services;

namespace PBA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureDependencies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                dataSource,
                npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddHttpClient();

        services.Configure<RssPollingOptions>(configuration.GetSection(RssPollingOptions.SectionName));
        services.AddHttpClient<RssFeedReader>();
        services.AddScoped<IRssFeedReader, RssFeedReader>();
        services.AddHostedService<RssPollingService>();

        services.Configure<SidecarOptions>(configuration.GetSection(SidecarOptions.SectionName));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ISidecarClient, SidecarClient>();
        services.AddHostedService<AiConnectionsService>();

        services.Configure<BlogConnectorOptions>(configuration.GetSection(BlogConnectorOptions.SectionName));
        services.AddKeyedScoped<IPlatformConnector, BlogConnector>(PBA.Domain.Enums.Platform.Blog);

        services.AddScoped<IContentPublisher, ContentPublisher>();
        services.AddScoped<IContentScheduler, HangfireContentScheduler>();
        services.AddHostedService<ScheduledPublishReconciler>();

        services.AddScoped<IFeedSeedService, FeedSeedService>();

        return services;
    }
}
