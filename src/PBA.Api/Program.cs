using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.PostgreSql;
using PBA.Api.Endpoints;
using PBA.Api.Hubs;
using PBA.Api.Services;
using PBA.Application;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationDependencies();
builder.Services.AddInfrastructureDependencies(builder.Configuration);

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

app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.MapIdeaEndpoints();
app.MapIdeaSourceEndpoints();
app.MapContentEndpoints();
app.MapFeedEndpoints();

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
}

app.Run();

public partial class Program { }
