using System.Text.Json.Serialization;
using ModelContextProtocol;
using PersonalBrandAssistant.Api.Endpoints;
using PersonalBrandAssistant.Api.Handlers;
using PersonalBrandAssistant.Api.Middleware;
using PersonalBrandAssistant.Application;
using PersonalBrandAssistant.Infrastructure;
using Serilog;

var isMcpMode = args.Contains("--mcp");

if (isMcpMode)
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "pba", Version = "1.0.0" };
        })
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .WriteTo.File("logs/mcp-.log", rollingInterval: RollingInterval.Day)
        .CreateLogger());

    var app = builder.Build();
    await app.RunAsync();
}
else
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

    builder.Host.UseSerilog((context, loggerConfiguration) =>
        loggerConfiguration.ReadFrom.Configuration(context.Configuration));

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddSignalR();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAngularDev", policy =>
        {
            policy.WithOrigins("http://localhost:4200", "http://localhost:4201")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();
    app.UseCors("AllowAngularDev");
    app.UseMiddleware<ApiKeyMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.MapHealthEndpoints();
    app.MapContentEndpoints();
    app.MapWorkflowEndpoints();
    app.MapApprovalEndpoints();
    app.MapSchedulingEndpoints();
    app.MapNotificationEndpoints();
    app.MapAgentEndpoints();
    app.MapMediaEndpoints();
    app.MapPlatformEndpoints();
    app.MapContentPipelineEndpoints();
    app.MapRepurposingEndpoints();
    app.MapCalendarEndpoints();
    app.MapBrandVoiceEndpoints();
    app.MapTrendEndpoints();
    app.MapAnalyticsEndpoints();
    app.MapSocialEndpoints();
    app.MapContentIdeaEndpoints();
    app.MapIntegrationEndpoints();
    app.MapEventEndpoints();
    app.MapAutomationEndpoints();
    app.MapBlogChatEndpoints();
    app.MapSubstackPrepEndpoints();
    app.MapBlogPublishEndpoints();

    app.Run();
}

public partial class Program { }
