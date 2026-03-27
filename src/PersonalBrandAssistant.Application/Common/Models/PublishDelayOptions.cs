namespace PersonalBrandAssistant.Application.Common.Models;

public class PublishDelayOptions
{
    public const string SectionName = "PublishDelay";

    public TimeSpan DefaultSubstackToBlogDelay { get; set; } = TimeSpan.FromDays(7);
    public bool RequiresConfirmation { get; set; } = true;
}
