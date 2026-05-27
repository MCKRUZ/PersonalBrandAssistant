using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Commands;

public static class RetryPlatformPublish
{
    public record Command(Guid ContentId, Platform Platform) : IRequest<Result>;

    internal sealed class Handler(
        IAppDbContext db,
        IPublishRetryHandler retryHandler) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var record = await db.ContentPlatformPublishes
                .FirstOrDefaultAsync(p =>
                    p.ContentId == request.ContentId
                    && p.Platform == request.Platform
                    && p.Status == PublishStatus.Failed,
                    cancellationToken);

            if (record is null)
                return Result.NotFound($"No failed publish found for {request.Platform}");

            await retryHandler.RetryAsync(record.Id, cancellationToken);
            return Result.Success();
        }
    }
}
