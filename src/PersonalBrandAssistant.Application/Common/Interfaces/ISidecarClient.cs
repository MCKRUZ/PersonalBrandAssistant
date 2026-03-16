using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ISidecarClient
{
    Task<SidecarSession> ConnectAsync(CancellationToken ct);

    IAsyncEnumerable<SidecarEvent> SendTaskAsync(string task, string? sessionId, CancellationToken ct);

    Task AbortAsync(string? sessionId, CancellationToken ct);

    bool IsConnected { get; }
}
