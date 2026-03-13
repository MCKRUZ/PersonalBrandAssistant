using MediatR;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Features.Content.Commands.CreateContent;

public sealed class CreateContentCommandHandler : IRequestHandler<CreateContentCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _dbContext;

    public CreateContentCommandHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Guid>> Handle(CreateContentCommand request, CancellationToken cancellationToken)
    {
        var content = ContentEntity.Create(
            request.ContentType,
            request.Body,
            request.Title,
            request.TargetPlatforms);

        if (request.Metadata is not null)
        {
            content.Metadata = request.Metadata;
        }

        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(content.Id);
    }
}
