namespace PBA.Infrastructure.Connectors;

public sealed class BlogConnectorOptions
{
    public const string SectionName = "BlogConnector";

    public required string RepoPath { get; init; }
    public required string TemplatePath { get; init; }
    public string Author { get; init; } = "Matt Kruczek";
    public string RemoteName { get; init; } = "origin";
    public string Branch { get; init; } = "main";
    public string BaseUrl { get; init; } = "https://matthewkruczek.ai";
}
