using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Features.Settings.Queries.GetAutonomySettings;

public sealed class GetAutonomySettingsQueryHandler
    : IRequestHandler<GetAutonomySettingsQuery, Result<AutonomySettingsResponse>>
{
    private readonly IApplicationDbContext _dbContext;

    public GetAutonomySettingsQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AutonomySettingsResponse>> Handle(
        GetAutonomySettingsQuery request,
        CancellationToken cancellationToken)
    {
        var config = await _dbContext.AutonomyConfigurations
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            return Result<AutonomySettingsResponse>.Success(AutonomySettingsResponse.Default);
        }

        return Result<AutonomySettingsResponse>.Success(new AutonomySettingsResponse(
            config.Id,
            config.GlobalLevel,
            config.AutoPublishEnabled,
            config.RequireApprovalForSocial,
            config.MaxAutoPostsPerDay,
            config.DefaultTone,
            config.AutoScheduleEnabled,
            config.AutoPublishThreshold));
    }
}
