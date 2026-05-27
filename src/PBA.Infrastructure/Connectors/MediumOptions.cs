namespace PBA.Infrastructure.Connectors;

public sealed class MediumOptions
{
    public const string SectionName = "Publishing:Medium";

    public bool Enabled { get; init; }
    public string DefaultPublishStatus { get; init; } = "draft";
}
