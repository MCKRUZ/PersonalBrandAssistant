using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;

namespace PBA.Application.Features.Ideas.Commands;

public static class DeleteIdeaSource
{
    public record Command(Guid Id) : IRequest<Result>;

    internal sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var source = await db.IdeaSources.FindAsync([request.Id], cancellationToken);

            if (source is null)
                return Result.NotFound($"IdeaSource {request.Id} not found");

            db.IdeaSources.Remove(source);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}
