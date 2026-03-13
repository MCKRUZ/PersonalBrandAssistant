using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Features.Content.Queries.GetContent;

public sealed class GetContentQueryHandler : IRequestHandler<GetContentQuery, Result<ContentEntity>>
{
    private readonly IApplicationDbContext _dbContext;

    public GetContentQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ContentEntity>> Handle(GetContentQuery request, CancellationToken cancellationToken)
    {
        var content = await _dbContext.Contents
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        return content is null
            ? Result<ContentEntity>.NotFound($"Content with ID {request.Id} not found.")
            : Result<ContentEntity>.Success(content);
    }
}
