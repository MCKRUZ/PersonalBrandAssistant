namespace PBA.Application.Features.Digests.Dtos;

public record DigestItemDto
{
    public Guid IdeaId { get; init; }
    public int Rank { get; init; }
    public int Score { get; init; }
    public string WhyItMatters { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Url { get; init; }
}

public record DigestDto
{
    public Guid Id { get; init; }
    public DateOnly Date { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Intro { get; init; } = string.Empty;
    public int ItemCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<DigestItemDto> Items { get; init; } = [];
}
