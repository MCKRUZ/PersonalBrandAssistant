namespace PBA.Domain.Entities;

public class DigestItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DigestId { get; set; }
    public Guid IdeaId { get; set; }
    public int Rank { get; set; }
    public int Score { get; set; }
    public required string WhyItMatters { get; set; }

    public Digest? Digest { get; set; }
    public Idea? Idea { get; set; }
}
