namespace PBA.Domain.Entities;

public class SavedIdea
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid IdeaId { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public string? Notes { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> SuggestedPlatforms { get; set; } = [];
    public string? SuggestedAngle { get; set; }

    public Idea? Idea { get; set; }
}
