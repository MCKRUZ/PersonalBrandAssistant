using MediatR;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;
using PersonalBrandAssistant.Application.Features.Content.Commands.DeleteContent;
using PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;
using PersonalBrandAssistant.Application.Features.Content.Queries.GetContent;
using PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class ContentEndpoints
{
    public static void MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content").WithTags("Content");

        group.MapGet("/", ListContent);
        group.MapGet("/{id:guid}", GetContent);
        group.MapPost("/", CreateContent);
        group.MapPut("/{id:guid}", UpdateContent);
        group.MapDelete("/{id:guid}", DeleteContent);
    }

    private static async Task<IResult> ListContent(
        ISender sender,
        ContentType? contentType = null,
        ContentStatus? status = null,
        int pageSize = 20,
        string? cursor = null)
    {
        var query = new ListContentQuery(contentType, status, Math.Clamp(pageSize, 1, 50), cursor);
        var result = await sender.Send(query);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetContent(ISender sender, Guid id)
    {
        var query = new GetContentQuery(id);
        var result = await sender.Send(query);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateContent(ISender sender, CreateContentCommand command)
    {
        var result = await sender.Send(command);
        return result.ToCreatedHttpResult("/api/content");
    }

    private static async Task<IResult> UpdateContent(ISender sender, Guid id, UpdateContentCommand command)
    {
        var updatedCommand = command with { Id = id };
        var result = await sender.Send(updatedCommand);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteContent(ISender sender, Guid id)
    {
        var command = new DeleteContentCommand(id);
        var result = await sender.Send(command);
        if (result.IsSuccess) return Results.NoContent();
        return result.ToHttpResult();
    }
}
