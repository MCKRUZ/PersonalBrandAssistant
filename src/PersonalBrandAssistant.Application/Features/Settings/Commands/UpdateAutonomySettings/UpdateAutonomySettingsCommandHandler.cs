using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Application.Features.Settings.Commands.UpdateAutonomySettings;

public sealed class UpdateAutonomySettingsCommandHandler
    : IRequestHandler<UpdateAutonomySettingsCommand, Result<AutonomySettingsResponse>>
{
    private readonly IApplicationDbContext _dbContext;

    public UpdateAutonomySettingsCommandHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AutonomySettingsResponse>> Handle(
        UpdateAutonomySettingsCommand request,
        CancellationToken cancellationToken)
    {
        var config = await _dbContext.AutonomyConfigurations
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = AutonomyConfiguration.CreateDefault();
            _dbContext.AutonomyConfigurations.Add(config);
        }

        config.UpdateSettings(
            request.GlobalLevel,
            request.AutoPublishEnabled,
            request.RequireApprovalForSocial,
            request.MaxAutoPostsPerDay,
            request.DefaultTone,
            request.AutoScheduleEnabled,
            request.AutoPublishThreshold);

        await _dbContext.SaveChangesAsync(cancellationToken);

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
