using System.Text.Json.Serialization;
using PersonalBrandAssistant.Api.Endpoints;
using PersonalBrandAssistant.Api.Handlers;
using PersonalBrandAssistant.Api.Middleware;
using PersonalBrandAssistant.Application;
using PersonalBrandAssistant.Infrastructure;
using Serilog;

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
        policy.WithOrigins("http://localhost:4200")
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

app.Run();

public partial class Program { }
