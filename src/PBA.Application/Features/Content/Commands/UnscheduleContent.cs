using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Commands;

public static class UnscheduleContent
{
    public record Command(Guid ContentId) : IRequest<Result>;

    internal sealed class Handler(IAppDbContext db, IContentScheduler scheduler) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var content = await db.Contents.FindAsync([request.ContentId], cancellationToken);
            if (content is null)
                return Result.NotFound($"Content {request.ContentId} not found");

            var machine = ContentStateMachine.Create(content);
            try
            {
                await machine.FireAsync(ContentTrigger.Unschedule);
            }
            catch (InvalidOperationException)
            {
                return Result.Fail("Cannot unschedule content in its current status");
            }

            if (!string.IsNullOrEmpty(content.HangfireJobId))
                scheduler.CancelScheduledPublish(content.HangfireJobId);

            content.HangfireJobId = null;
            content.ScheduledAt = null;

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
    }
}
