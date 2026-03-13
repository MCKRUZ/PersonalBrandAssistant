using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
            .ExcludeFromDescription();

        app.MapGet("/health/ready", async (HealthCheckService healthCheckService) =>
        {
            var report = await healthCheckService.CheckHealthAsync();
            return report.Status == HealthStatus.Healthy
                ? Results.Ok(new { status = "Ready" })
                : Results.Json(new { status = "Unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).WithTags("Health");
    }
}
