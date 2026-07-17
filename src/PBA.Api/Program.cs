using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.PostgreSql;
using PBA.Api.Authentication;
using PBA.Api.Endpoints;
using PBA.Api.Hubs;
using PBA.Api.Services;
using PBA.Application;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationDependencies();
builder.Services.AddInfrastructureDependencies(builder.Configuration);

builder.Services.Configure<ExternalApiOptions>(
    builder.Configuration.GetSection(ExternalApiOptions.SectionName));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://localhost:4201")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSignalR();
builder.Services.AddScoped<IFeedNotifier, FeedNotifier>();

builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(o =>
        o.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"))));
builder.Services.AddHangfireServer();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

// Ensure the v1 active BrandRankingProfile exists (the ranker depends on it). Idempotent + race-safe;
// guarded so a pre-migration boot logs and continues rather than crashing startup.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IBrandRankingProfileSeedService>();
        await seeder.SeedAsync();
    }
    catch (Exception ex)
    {
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "BrandRankingProfile seed skipped at startup (schema may not be migrated yet).");
    }
}

app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.MapIdeaEndpoints();
app.MapIdeaSourceEndpoints();
app.MapContentEndpoints();
app.MapOAuthEndpoints();
app.MapPlatformEndpoints();
app.MapFeedEndpoints();
app.MapAnalyticsEndpoints();
app.MapChannelAnalyticsEndpoints();
app.MapDigestEndpoints();
app.MapBrandRankingProfileEndpoints();
app.MapExternalEndpoints();

app.MapHub<ContentHub>("/hubs/content");
app.MapHub<FeedHub>("/hubs/feed");

if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");

    app.MapPost("/api/feed/seed", async (IFeedSeedService seedService, CancellationToken ct) =>
    {
        var count = await seedService.SeedAsync(ct);
        return Results.Ok(new { seeded = count });
    });

    app.MapPost("/api/idea-sources/seed", async (IIdeaSourceSeedService seedService, CancellationToken ct) =>
    {
        var count = await seedService.SeedAsync(ct);
        return Results.Ok(new { seeded = count });
    });

    app.MapPost("/api/brand-ranking-profile/seed",
        async (IBrandRankingProfileSeedService seedService, CancellationToken ct) =>
        {
            var count = await seedService.SeedAsync(ct);
            return Results.Ok(new { seeded = count });
        });
}

app.Run();

public partial class Program { }
