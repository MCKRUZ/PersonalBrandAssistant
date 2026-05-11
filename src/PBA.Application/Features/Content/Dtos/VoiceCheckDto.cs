namespace PBA.Application.Features.Content.Dtos;

public record VoiceCheckDto
{
    public decimal Score { get; init; }
    public string Feedback { get; init; } = string.Empty;
}
