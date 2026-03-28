using System.Globalization;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Services.SocialServices;

public sealed class HumanScheduler : IHumanScheduler
{
    private readonly IDateTimeProvider _dateTime;

    public HumanScheduler(IDateTimeProvider dateTime)
    {
        _dateTime = dateTime;
    }

    public WeeklyBehaviorProfile GetCurrentProfile(EngagementTask task)
    {
        var now = _dateTime.UtcNow;
        var weekNumber = ISOWeek.GetWeekOfYear(now.DateTime);
        var seed = HashCode.Combine(task.Id, weekNumber, now.Year);
        var rng = new Random(seed);

        var morningStart = rng.Next(7, 11);
        var morningEnd = morningStart + rng.Next(2, 5);
        morningEnd = Math.Min(morningEnd, 12);

        var afternoonStart = rng.Next(13, 18);
        var afternoonEnd = afternoonStart + rng.Next(2, 5);
        afternoonEnd = Math.Min(afternoonEnd, 22);

        var activeWindows = new List<(int, int)> { (morningStart, morningEnd), (afternoonStart, afternoonEnd) };

        var allDays = Enum.GetValues<DayOfWeek>();
        var shuffled = allDays.OrderBy(_ => rng.Next()).ToArray();
        var dayCount = rng.Next(3, 6);
        var activeDays = new HashSet<DayOfWeek>(shuffled.Take(dayCount));

        var minActions = seed % 3 + 1;
        var maxActions = minActions + rng.Next(1, 3);

        var minDelaySeconds = 30 + rng.Next(0, 60);
        var maxDelaySeconds = 180 + rng.Next(0, 120);

        var skipProbability = 0.08 + rng.NextDouble() * 0.07;

        return new WeeklyBehaviorProfile(
            activeWindows.AsReadOnly(),
            activeDays,
            minActions,
            maxActions,
            TimeSpan.FromSeconds(minDelaySeconds),
            TimeSpan.FromSeconds(maxDelaySeconds),
            skipProbability);
    }

    public DateTimeOffset ComputeNextHumanExecution(EngagementTask task, DateTimeOffset baseCronNext)
    {
        var profile = GetCurrentProfile(task);
        var candidate = baseCronNext;

        // Advance to next valid active window/day if needed
        for (var attempts = 0; attempts < 168; attempts++) // max 1 week of hours
        {
            if (IsInActiveWindow(candidate, profile))
                break;
            candidate = AdvanceToNextWindow(candidate, profile);
        }

        // Add Gaussian jitter (mean=0, std=15min, clamped to [-5min, +45min])
        var now = _dateTime.UtcNow;
        var jitterSeed = HashCode.Combine(task.Id, candidate.DayOfYear, candidate.Hour);
        var jitterRng = new Random(jitterSeed);
        var jitterMinutes = GaussianRandom(jitterRng, 0, 15);
        jitterMinutes = Math.Clamp(jitterMinutes, -5, 45);
        candidate = candidate.AddMinutes(jitterMinutes);

        // Anti-pattern: if within 30 min of yesterday's execution time, add extra offset
        if (task.LastExecutedAt.HasValue)
        {
            var lastTimeOfDay = task.LastExecutedAt.Value.TimeOfDay;
            var candidateTimeOfDay = candidate.TimeOfDay;
            var timeDiff = Math.Abs((candidateTimeOfDay - lastTimeOfDay).TotalMinutes);
            if (timeDiff < 30)
            {
                var extraMinutes = 15 + jitterRng.Next(0, 46);
                candidate = candidate.AddMinutes(extraMinutes);
            }
        }

        // Never schedule in the past
        if (candidate <= now)
            candidate = now.AddMinutes(1 + jitterRng.Next(0, 15));

        return candidate;
    }

    public bool ShouldSkipExecution(EngagementTask task)
    {
        // Never double-skip
        if (task.SkippedLastExecution)
            return false;

        var now = _dateTime.UtcNow;
        var seed = HashCode.Combine(task.Id, now.DayOfYear, now.Hour);
        var rng = new Random(seed);
        var profile = GetCurrentProfile(task);

        return rng.NextDouble() < profile.SkipProbability;
    }

    public TimeSpan GetInterActionDelay(EngagementTask task)
    {
        var now = _dateTime.UtcNow;
        var seed = HashCode.Combine(task.Id, now.Ticks / TimeSpan.TicksPerSecond);
        var rng = new Random(seed);
        var profile = GetCurrentProfile(task);

        var seconds = profile.MinDelay.TotalSeconds +
                      rng.NextDouble() * (profile.MaxDelay.TotalSeconds - profile.MinDelay.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    public int GetActionsForSession(EngagementTask task)
    {
        var now = _dateTime.UtcNow;
        var seed = HashCode.Combine(task.Id, now.DayOfYear, now.Hour);
        var rng = new Random(seed);
        var profile = GetCurrentProfile(task);

        var actions = rng.Next(profile.MinActions, profile.MaxActions + 1);
        return Math.Min(actions, task.MaxActionsPerExecution);
    }

    private static bool IsInActiveWindow(DateTimeOffset time, WeeklyBehaviorProfile profile)
    {
        if (!profile.ActiveDays.Contains(time.DayOfWeek))
            return false;

        var hour = time.Hour;
        return profile.ActiveWindows.Any(w => hour >= w.StartHour && hour < w.EndHour);
    }

    private static DateTimeOffset AdvanceToNextWindow(DateTimeOffset time, WeeklyBehaviorProfile profile)
    {
        // Try next hour in current day
        var next = new DateTimeOffset(time.Year, time.Month, time.Day, time.Hour, 0, 0, time.Offset)
            .AddHours(1);

        // Check up to 7 days ahead
        for (var h = 0; h < 168; h++)
        {
            if (IsInActiveWindow(next, profile))
                return next.AddMinutes(time.Minute);
            next = next.AddHours(1);
        }

        // Fallback: return original + 1 hour
        return time.AddHours(1);
    }

    private static double GaussianRandom(Random rng, double mean, double stdDev)
    {
        // Box-Muller transform
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + stdDev * normal;
    }
}
