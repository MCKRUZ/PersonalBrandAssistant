using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.ContentStudio;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Commands;

public static class RestoreContent
{
    public record Command(Guid ContentId) : IRequest<Result>;

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var content = await db.Contents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == request.ContentId, cancellationToken);

            if (content is null)
                return Result.NotFound($"Content {request.ContentId} not found");

            var machine = ContentStateMachine.Create(content);
            try
            {
                await machine.FireAsync(ContentTrigger.Restore);
            }
            catch (InvalidOperationException)
            {
                return Result.Fail("Invalid status transition");
            }

            content.IsDeleted = false;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
    }
}
