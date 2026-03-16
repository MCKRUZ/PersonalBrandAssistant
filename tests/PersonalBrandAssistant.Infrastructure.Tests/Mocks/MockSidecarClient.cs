using System.Runtime.CompilerServices;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Tests.Mocks;

public sealed class MockSidecarClient : ISidecarClient
{
    public bool IsConnected => true;

    public Task<SidecarSession> ConnectAsync(CancellationToken ct)
        => Task.FromResult(new SidecarSession("mock-session", DateTimeOffset.UtcNow));

    public async IAsyncEnumerable<SidecarEvent> SendTaskAsync(
        string task, string? systemPrompt, string? sessionId, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ChatEvent("assistant", "Mock response", null, null);
        yield return new TaskCompleteEvent("mock-session", 100, 50);
        await Task.CompletedTask;
    }

    public Task AbortAsync(string? sessionId, CancellationToken ct)
        => Task.CompletedTask;
}
