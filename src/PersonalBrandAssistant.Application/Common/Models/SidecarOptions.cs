namespace PersonalBrandAssistant.Application.Common.Models;

public class SidecarOptions
{
    public const string SectionName = "Sidecar";
    public string WebSocketUrl { get; set; } = "ws://localhost:3001/ws";
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int ReconnectDelaySeconds { get; set; } = 5;
}
