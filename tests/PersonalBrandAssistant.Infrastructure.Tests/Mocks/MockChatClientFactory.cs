using Microsoft.Extensions.AI;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Tests.Mocks;

public sealed class MockChatClientFactory : IChatClientFactory
{
    private readonly MockChatClient _client;

    public MockChatClientFactory(MockChatClient? client = null)
    {
        _client = client ?? new MockChatClient();
    }

    public IChatClient CreateClient(ModelTier tier) => _client;
    public IChatClient CreateStreamingClient(ModelTier tier) => _client;
}
