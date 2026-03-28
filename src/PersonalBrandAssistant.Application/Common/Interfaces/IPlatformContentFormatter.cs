using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IPlatformContentFormatter
{
    PlatformType Platform { get; }
    Result<PlatformContent> FormatAndValidate(Content content);
}
