namespace PBA.Infrastructure.Configuration;

public sealed class SubstackOptions
{
    public const string SectionName = "Publishing:Substack";

    public bool Enabled { get; init; }
    public string PublicationSlug { get; init; } = string.Empty;
    public string DefaultAudience { get; init; } = "everyone";
    public bool SendEmailOnPublish { get; init; } = true;
}
