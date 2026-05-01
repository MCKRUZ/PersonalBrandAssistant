using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Domain.Entities;

public class AutonomyConfiguration : AuditableEntityBase
{
    private AutonomyConfiguration()
    {
        Id = Guid.Empty;
    }

    public AutonomyLevel GlobalLevel { get; set; } = AutonomyLevel.Draft;
    public bool AutoPublishEnabled { get; private set; }
    public bool RequireApprovalForSocial { get; private set; } = true;
    public int AutoPublishThreshold { get; private set; } = 90;
    public int MaxAutoPostsPerDay { get; private set; } = 5;
    public string DefaultTone { get; private set; } = "Professional";
    public bool AutoScheduleEnabled { get; private set; }
    public List<ContentTypeOverride> ContentTypeOverrides { get; set; } = [];
    public List<PlatformOverride> PlatformOverrides { get; set; } = [];
    public List<ContentTypePlatformOverride> ContentTypePlatformOverrides { get; set; } = [];

    public static AutonomyConfiguration CreateDefault() => new();

    public void UpdateSettings(
        AutonomyLevel globalLevel,
        bool autoPublishEnabled,
        bool requireApprovalForSocial,
        int maxAutoPostsPerDay,
        string defaultTone,
        bool autoScheduleEnabled,
        int? autoPublishThreshold = null)
    {
        GlobalLevel = globalLevel;
        AutoPublishEnabled = autoPublishEnabled;
        RequireApprovalForSocial = requireApprovalForSocial;
        MaxAutoPostsPerDay = maxAutoPostsPerDay;
        DefaultTone = defaultTone;
        AutoScheduleEnabled = autoScheduleEnabled;
        if (autoPublishThreshold is not null)
            AutoPublishThreshold = Math.Clamp(autoPublishThreshold.Value, 0, 100);
    }

    public AutonomyLevel ResolveLevel(ContentType type, PlatformType? platform)
    {
        if (platform is not null)
        {
            var ctpOverride = ContentTypePlatformOverrides
                .FirstOrDefault(o => o.ContentType == type && o.PlatformType == platform.Value);
            if (ctpOverride is not null)
                return ctpOverride.Level;

            var platformOverride = PlatformOverrides
                .FirstOrDefault(o => o.PlatformType == platform.Value);
            if (platformOverride is not null)
                return platformOverride.Level;
        }

        var contentTypeOverride = ContentTypeOverrides
            .FirstOrDefault(o => o.ContentType == type);
        if (contentTypeOverride is not null)
            return contentTypeOverride.Level;

        return GlobalLevel;
    }
}
