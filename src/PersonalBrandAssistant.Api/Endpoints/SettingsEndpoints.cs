using MediatR;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Features.Settings.Commands.UpdateAutonomySettings;
using PersonalBrandAssistant.Application.Features.Settings.Queries.GetAutonomySettings;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");

        group.MapGet("/autonomy", GetAutonomySettings);
        group.MapPut("/autonomy", UpdateAutonomySettings);
    }

    private static async Task<IResult> GetAutonomySettings(ISender sender)
    {
        var result = await sender.Send(new GetAutonomySettingsQuery());
        return result.ToHttpResult();
    }

    private static async Task<IResult> UpdateAutonomySettings(
        ISender sender,
        UpdateAutonomySettingsCommand command)
    {
        var result = await sender.Send(command);
        return result.ToHttpResult();
    }
}
