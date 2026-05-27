using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Commands;

public static class ScheduleContent
{
    public record Command(Guid ContentId, DateTimeOffset ScheduledAt, IReadOnlyList<Platform>? TargetPlatforms = null) : IRequest<Result>;

    internal sealed class Handler(IAppDbContext db, IContentScheduler scheduler) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var content = await db.Contents.FindAsync([request.ContentId], cancellationToken);
            if (content is null)
                return Result.NotFound($"Content {request.ContentId} not found");

            content.ScheduledAt = request.ScheduledAt;

            if (request.TargetPlatforms is { Count: > 0 })
                content.TargetPlatforms = request.TargetPlatforms.ToList();

            var machine = ContentStateMachine.Create(content);
            try
            {
                await machine.FireAsync(ContentTrigger.Schedule);
            }
            catch (InvalidOperationException)
            {
                return Result.Fail("Cannot schedule content in its current status");
            }

            var jobId = scheduler.SchedulePublish(content.Id, request.ScheduledAt);
            content.HangfireJobId = jobId;

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
    }
}
