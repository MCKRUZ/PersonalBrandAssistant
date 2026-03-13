using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Infrastructure.Services;

public class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
