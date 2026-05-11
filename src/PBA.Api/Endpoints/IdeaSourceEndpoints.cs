using MediatR;
using PBA.Api.Extensions;
using PBA.Application.Features.Ideas.Commands;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Application.Features.Ideas.Queries;

namespace PBA.Api.Endpoints;

public static class IdeaSourceEndpoints
{
    public static void MapIdeaSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/idea-sources").WithTags("IdeaSources");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListIdeaSources.Query(), ct);
            return result.ToApiResult();
        });

        group.MapPost("/", async (IdeaSourceRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateIdeaSource.Command
            {
                Name = body.Name,
                Type = body.Type,
                FeedUrl = body.FeedUrl,
                ApiUrl = body.ApiUrl,
                Category = body.Category,
                PollIntervalMinutes = body.PollIntervalMinutes
            };
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/idea-sources/{result.Value}", result.Value)
                : result.ToApiResult();
        });

        group.MapPut("/{id:guid}", async (
            Guid id, IdeaSourceRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateIdeaSource.Command
            {
                Id = id,
                Name = body.Name,
                FeedUrl = body.FeedUrl,
                ApiUrl = body.ApiUrl,
                Category = body.Category,
                PollIntervalMinutes = body.PollIntervalMinutes,
                IsEnabled = body.IsEnabled
            };
            var result = await sender.Send(command, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteIdeaSource.Command(id), ct);
            return result.IsSuccess ? Results.NoContent() : result.ToApiResult();
        });

        group.MapPost("/refresh", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RefreshIdeaSources.Command(), ct);
            return result.ToApiResult();
        });
    }
}
