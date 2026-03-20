using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public record ScrapeResult(string Markdown, string? ImageUrl);

public interface IArticleScraper
{
    Task<Result<ScrapeResult>> ScrapeAsync(string url, CancellationToken ct);
}
