using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace PersonalBrandAssistant.Infrastructure.Tests.Mocks;

public sealed class MockChatClient : IChatClient
{
    private readonly string _responseText;
    private readonly int _inputTokens;
    private readonly int _outputTokens;
    private int _callCount;
    private readonly int _failFirstNCalls;

    public MockChatClient(
        string responseText = "Mock response",
        int inputTokens = 100,
        int outputTokens = 50,
        int failFirstNCalls = 0)
    {
        _responseText = responseText;
        _inputTokens = inputTokens;
        _outputTokens = outputTokens;
        _failFirstNCalls = failFirstNCalls;
    }

    public int CallCount => _callCount;

    public ChatClientMetadata Metadata { get; } = new("MockChatClient", null, "mock-model");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var count = Interlocked.Increment(ref _callCount);
        if (count <= _failFirstNCalls)
            throw new HttpRequestException("Simulated transient failure");

        await Task.CompletedTask;

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseText))
        {
            Usage = new UsageDetails
            {
                InputTokenCount = _inputTokens,
                OutputTokenCount = _outputTokens,
            },
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var count = Interlocked.Increment(ref _callCount);
        if (count <= _failFirstNCalls)
            throw new HttpRequestException("Simulated transient failure");

        var words = _responseText.Split(' ');
        foreach (var word in words)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
