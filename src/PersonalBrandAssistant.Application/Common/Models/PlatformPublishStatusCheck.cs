using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record PlatformPublishStatusCheck(PlatformPublishStatus Status, string? PostUrl, string? ErrorMessage);
