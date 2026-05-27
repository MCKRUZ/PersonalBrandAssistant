namespace PBA.Application.Features.Content.Dtos;

public record StoreCredentialsRequest
{
    public string? Token { get; init; }
    public string? Email { get; init; }
    public string? Password { get; init; }
}
