namespace PBA.Domain.Entities;

public class Digest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public required string Title { get; set; }
    public required string Intro { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<DigestItem> Items { get; set; } = [];
}
