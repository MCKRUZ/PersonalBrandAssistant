using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.UpdateContent;

public sealed class UpdateContentCommandHandler : IRequestHandler<UpdateContentCommand, Result<Unit>>
{
    private readonly IApplicationDbContext _dbContext;

    public UpdateContentCommandHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Unit>> Handle(UpdateContentCommand request, CancellationToken cancellationToken)
    {
        var content = await _dbContext.Contents.FirstOrDefaultAsync(
            c => c.Id == request.Id, cancellationToken);

        if (content is null)
        {
            return Result<Unit>.NotFound($"Content with ID {request.Id} not found.");
        }

        if (content.Status is not (ContentStatus.Draft or ContentStatus.Review))
        {
            return Result<Unit>.Failure(ErrorCode.ValidationFailed, "Content is not in an editable state.");
        }

        if (request.Title is not null) content.Title = request.Title;
        if (request.Body is not null) content.Body = request.Body;
        if (request.TargetPlatforms is not null) content.TargetPlatforms = request.TargetPlatforms;
        if (request.Metadata is not null) content.Metadata = request.Metadata;

        content.Version = request.Version;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<Unit>.Conflict("Content was modified by another process.");
        }

        return Result<Unit>.Success(Unit.Value);
    }
}
