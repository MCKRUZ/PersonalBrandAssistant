namespace PBA.Domain.Entities;

using PBA.Domain.Enums;

public class Digest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public DigestKind Kind { get; set; } = DigestKind.Main;
    public required string Title { get; set; }
    public required string Intro { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<DigestItem> Items { get; set; } = [];
}
