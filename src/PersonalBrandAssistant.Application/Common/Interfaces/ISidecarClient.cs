using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ISidecarClient
{
    Task<SidecarSession> ConnectAsync(CancellationToken ct);

    Task<SidecarSession> NewSessionAsync(CancellationToken ct);

    IAsyncEnumerable<SidecarEvent> SendTaskAsync(string task, string? systemPrompt, string? sessionId, string? modelId, CancellationToken ct);

    Task AbortAsync(string? sessionId, CancellationToken ct);

    bool IsConnected { get; }
}
