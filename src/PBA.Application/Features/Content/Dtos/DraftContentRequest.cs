namespace PBA.Application.Features.Content.Dtos;

public record DraftContentRequest
{
    public string Action { get; init; } = string.Empty;
    public string? Instructions { get; init; }
    public string? ToneName { get; init; }
}
