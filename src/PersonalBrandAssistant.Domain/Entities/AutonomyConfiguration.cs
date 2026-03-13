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

    public AutonomyLevel GlobalLevel { get; set; } = AutonomyLevel.Manual;
    public List<ContentTypeOverride> ContentTypeOverrides { get; set; } = [];
    public List<PlatformOverride> PlatformOverrides { get; set; } = [];
    public List<ContentTypePlatformOverride> ContentTypePlatformOverrides { get; set; } = [];

    public static AutonomyConfiguration CreateDefault() => new();

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
