namespace PBA.Application.Features.Digests.Dtos;

public record DigestSummaryDto
{
    public Guid Id { get; init; }
    public DateOnly Date { get; init; }
    public string Title { get; init; } = string.Empty;
    public int ItemCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
