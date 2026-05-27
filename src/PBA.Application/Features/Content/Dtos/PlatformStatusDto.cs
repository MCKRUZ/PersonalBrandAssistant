using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Dtos;

public record PlatformStatusDto
{
    public Platform Platform { get; init; }
    public bool IsConnected { get; init; }
    public string Status { get; init; } = "NotConfigured";
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastPublishDate { get; init; }
    public PlatformCapabilities? Capabilities { get; init; }
}
