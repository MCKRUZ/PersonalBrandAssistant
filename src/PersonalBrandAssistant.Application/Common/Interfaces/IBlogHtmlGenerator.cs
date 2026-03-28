using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IBlogHtmlGenerator
{
    Task<Result<BlogHtmlResult>> GenerateAsync(Guid contentId, CancellationToken ct);
}
