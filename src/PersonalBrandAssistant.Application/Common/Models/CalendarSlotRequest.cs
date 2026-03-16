using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record CalendarSlotRequest(
    DateTimeOffset ScheduledAt,
    PlatformType Platform);
