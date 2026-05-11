namespace PBA.Domain.Entities;

using PBA.Domain.Enums;

public class IdeaSource
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public IdeaSourceType Type { get; set; } = IdeaSourceType.RSS;
    public string? FeedUrl { get; set; }
    public string? ApiUrl { get; set; }
    public string Category { get; set; } = string.Empty;
    public int PollIntervalMinutes { get; set; } = 30;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastPolledAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }

    // List<T> backing for EF Core change tracker compatibility (array is fixed-size)
    public IReadOnlyList<Idea> Ideas { get; set; } = new List<Idea>();
}
