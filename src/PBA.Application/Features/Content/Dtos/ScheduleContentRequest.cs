namespace PBA.Application.Features.Content.Dtos;

public record ScheduleContentRequest
{
    public DateTimeOffset ScheduledAt { get; init; }
}
