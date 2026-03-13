namespace PersonalBrandAssistant.Domain.ValueObjects;

public class PlatformSettings
{
    public List<string> DefaultHashtags { get; set; } = [];
    public int? MaxPostLength { get; set; }
    public bool AutoCrossPost { get; set; }
}
