using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.PostgreSql;
using PBA.Api.Endpoints;
using PBA.Api.Hubs;
using PBA.Application;
using PBA.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationDependencies();
builder.Services.AddInfrastructureDependencies(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSignalR();

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

app.MapHub<ContentHub>("/hubs/content");

if (app.Environment.IsDevelopment())
    app.UseHangfireDashboard("/hangfire");

app.Run();

public partial class Program { }
