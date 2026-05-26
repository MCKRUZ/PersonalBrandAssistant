namespace PBA.Domain.Entities;

public class BrandProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Personality { get; set; }
    public required string Tone { get; set; }
    public List<string> Topics { get; set; } = [];
    public List<string> Vocabulary { get; set; } = [];
    public List<string> AvoidWords { get; set; } = [];
    public string? ExamplePosts { get; set; }
    public string? LearningLog { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
