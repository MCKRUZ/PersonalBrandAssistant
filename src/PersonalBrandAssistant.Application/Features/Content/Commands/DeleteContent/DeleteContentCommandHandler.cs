using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.DeleteContent;

public sealed class DeleteContentCommandHandler : IRequestHandler<DeleteContentCommand, Result<Unit>>
{
    private readonly IApplicationDbContext _dbContext;

    public DeleteContentCommandHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Unit>> Handle(DeleteContentCommand request, CancellationToken cancellationToken)
    {
        var content = await _dbContext.Contents.FirstOrDefaultAsync(
            c => c.Id == request.Id, cancellationToken);

        if (content is null)
        {
            return Result<Unit>.NotFound($"Content with ID {request.Id} not found.");
        }

        if (content.Status == ContentStatus.Archived)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        try
        {
            content.TransitionTo(ContentStatus.Archived);
        }
        catch (InvalidOperationException ex)
        {
            return Result<Unit>.Failure(Common.Errors.ErrorCode.ValidationFailed, ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}
