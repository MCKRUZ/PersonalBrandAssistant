namespace PBA.Application.Features.Feed.Dtos;

public record ActOnFeedItemRequest
{
    public string Action { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string>? AdditionalData { get; init; }
}
