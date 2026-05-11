using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Commands;

public static class DeleteContent
{
    public record Command(Guid ContentId) : IRequest<Result>;

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var content = await db.Contents.FindAsync([request.ContentId], cancellationToken);
            if (content is null)
                return Result.NotFound($"Content {request.ContentId} not found");

            var machine = ContentStateMachine.Create(content);
            try
            {
                await machine.FireAsync(ContentTrigger.Archive);
            }
            catch (InvalidOperationException)
            {
                return Result.Fail($"Cannot archive content in {content.Status} status");
            }

            content.IsDeleted = true;

            var children = await db.Contents
                .Where(c => c.ParentContentId == request.ContentId && c.Status != ContentStatus.Published)
                .ToListAsync(cancellationToken);

            foreach (var child in children)
            {
                var childMachine = ContentStateMachine.Create(child);
                try
                {
                    await childMachine.FireAsync(ContentTrigger.Archive);
                    child.IsDeleted = true;
                }
                catch (InvalidOperationException)
                {
                    // Best effort — skip children that can't be archived
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}
