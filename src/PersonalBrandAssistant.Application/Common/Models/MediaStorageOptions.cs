namespace PersonalBrandAssistant.Application.Common.Models;

public class MediaStorageOptions
{
    public string BasePath { get; set; } = "./media";
    public string? SigningKey { get; set; }
}
