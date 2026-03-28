namespace PersonalBrandAssistant.Application.Common.Models;

public sealed record WeeklyBehaviorProfile(
    IReadOnlyList<(int StartHour, int EndHour)> ActiveWindows,
    IReadOnlySet<DayOfWeek> ActiveDays,
    int MinActions,
    int MaxActions,
    TimeSpan MinDelay,
    TimeSpan MaxDelay,
    double SkipProbability);
