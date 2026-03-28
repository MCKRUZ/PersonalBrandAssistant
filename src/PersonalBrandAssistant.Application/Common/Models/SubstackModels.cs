namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>A Substack post parsed from RSS feed.</summary>
public record SubstackPost(
    string Title, string Url, DateTimeOffset PublishedAt, string? Summary);
