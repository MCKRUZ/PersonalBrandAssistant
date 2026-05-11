using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Commands;

public static class DismissIdea
{
    public record Command(Guid IdeaId) : IRequest<Result>;

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var idea = await db.Ideas
                .Include(i => i.SavedDetails)
                .FirstOrDefaultAsync(i => i.Id == request.IdeaId, cancellationToken);

            if (idea is null)
                return Result.NotFound($"Idea {request.IdeaId} not found");

            idea.Status = IdeaStatus.Dismissed;

            if (idea.SavedDetails is not null)
                db.SavedIdeas.Remove(idea.SavedDetails);

            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}
