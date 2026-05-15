using MediatR;
using PBA.Domain.Common;

namespace PBA.Application.Features.Feed.Commands;

public static class BatchAct
{
    public record Command(IReadOnlyList<Guid> Ids, string Action) : IRequest<Result<BatchActResponse>>;

    public record BatchActResponse(int SuccessCount, IReadOnlyList<BatchActFailure> Failures);

    public record BatchActFailure(Guid Id, string Reason);

    public sealed class Handler(ISender sender) : IRequestHandler<Command, Result<BatchActResponse>>
    {
        public async Task<Result<BatchActResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var successCount = 0;
            var failures = new List<BatchActFailure>();

            foreach (var id in request.Ids)
            {
                var result = await sender.Send(new ActOnFeedItem.Command(id, request.Action), cancellationToken);
                if (result.IsSuccess)
                {
                    successCount++;
                }
                else
                {
                    failures.Add(new BatchActFailure(id, string.Join("; ", result.Errors)));
                }
            }

            return new BatchActResponse(successCount, failures);
        }
    }
}
