using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

public class ChatClientFactoryTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory;
    private readonly Mock<ILogger<ChatClientFactory>> _logger;

    public ChatClientFactoryTests()
    {
        _scopeFactory = new Mock<IServiceScopeFactory>();
        _logger = new Mock<ILogger<ChatClientFactory>>();
    }

    private IConfiguration BuildConfig(
        string apiKey = "test-api-key",
        Dictionary<string, string?>? models = null)
    {
        var configData = new Dictionary<string, string?>
        {
            ["AgentOrchestration:ApiKey"] = apiKey
        };

        if (models is not null)
        {
            foreach (var (tier, modelId) in models)
            {
                configData[$"AgentOrchestration:Models:{tier}"] = modelId;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private ChatClientFactory CreateFactory(
        string apiKey = "test-api-key",
        Dictionary<string, string?>? models = null)
    {
        return new ChatClientFactory(
            BuildConfig(apiKey, models),
            _scopeFactory.Object,
            _logger.Object);
    }

    [Theory]
    [InlineData(ModelTier.Fast, "claude-haiku-4-5")]
    [InlineData(ModelTier.Standard, "claude-sonnet-4-5-20250929")]
    [InlineData(ModelTier.Advanced, "claude-opus-4-6")]
    public void CreateClient_MapsDefaultTierToCorrectModelId(ModelTier tier, string expectedModelId)
    {
        var factory = CreateFactory();

        var modelId = factory.GetModelId(tier);

        Assert.Equal(expectedModelId, modelId);
    }

    [Fact]
    public void CreateClient_ReturnsWrappedChatClient()
    {
        var factory = CreateFactory();

        var client = factory.CreateClient(ModelTier.Standard);

        Assert.NotNull(client);
        Assert.IsType<TokenTrackingDecorator>(client);
    }

    [Fact]
    public void CreateClient_UsesConfiguredModelIds()
    {
        var models = new Dictionary<string, string?>
        {
            ["Fast"] = "custom-fast-model",
            ["Standard"] = "custom-standard-model",
            ["Advanced"] = "custom-advanced-model"
        };
        var factory = CreateFactory(models: models);

        Assert.Equal("custom-fast-model", factory.GetModelId(ModelTier.Fast));
        Assert.Equal("custom-standard-model", factory.GetModelId(ModelTier.Standard));
        Assert.Equal("custom-advanced-model", factory.GetModelId(ModelTier.Advanced));
    }

    [Fact]
    public void Constructor_ThrowsWhenApiKeyIsMissing()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CreateFactory(apiKey: ""));
    }

    [Fact]
    public void CreateStreamingClient_ReturnsSameClientAsCreateClient()
    {
        var factory = CreateFactory();

        var client = factory.CreateClient(ModelTier.Fast);
        var streamingClient = factory.CreateStreamingClient(ModelTier.Fast);

        Assert.Same(client, streamingClient);
    }

    [Fact]
    public void CreateClient_CachesClientsPerTier()
    {
        var factory = CreateFactory();

        var first = factory.CreateClient(ModelTier.Standard);
        var second = factory.CreateClient(ModelTier.Standard);

        Assert.Same(first, second);
    }

    [Fact]
    public void CreateClient_DifferentTiersReturnDifferentClients()
    {
        var factory = CreateFactory();

        var fast = factory.CreateClient(ModelTier.Fast);
        var standard = factory.CreateClient(ModelTier.Standard);

        Assert.NotSame(fast, standard);
    }
}

public class AgentExecutionContextTests
{
    [Fact]
    public void CurrentExecutionId_DefaultsToNull()
    {
        Assert.Null(AgentExecutionContext.CurrentExecutionId);
    }

    [Fact]
    public void CurrentExecutionId_CanBeSetAndRead()
    {
        var id = Guid.NewGuid();
        AgentExecutionContext.CurrentExecutionId = id;

        Assert.Equal(id, AgentExecutionContext.CurrentExecutionId);

        // Clean up
        AgentExecutionContext.CurrentExecutionId = null;
    }

    [Fact]
    public async Task CurrentExecutionId_IsIsolatedPerAsyncFlow()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        Guid? capturedInTask1 = null;
        Guid? capturedInTask2 = null;

        var task1 = Task.Run(() =>
        {
            AgentExecutionContext.CurrentExecutionId = id1;
            Thread.Sleep(50);
            capturedInTask1 = AgentExecutionContext.CurrentExecutionId;
        });

        var task2 = Task.Run(() =>
        {
            AgentExecutionContext.CurrentExecutionId = id2;
            Thread.Sleep(50);
            capturedInTask2 = AgentExecutionContext.CurrentExecutionId;
        });

        await Task.WhenAll(task1, task2);

        Assert.Equal(id1, capturedInTask1);
        Assert.Equal(id2, capturedInTask2);
    }
}

public class TokenTrackingDecoratorTests
{
    private readonly Mock<ITokenTracker> _tokenTracker;
    private readonly Mock<IServiceScopeFactory> _scopeFactory;

    public TokenTrackingDecoratorTests()
    {
        _tokenTracker = new Mock<ITokenTracker>();
        _scopeFactory = new Mock<IServiceScopeFactory>();

        var scope = new Mock<IServiceScope>();
        var asyncScope = new Mock<IAsyncDisposable>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ITokenTracker)))
            .Returns(_tokenTracker.Object);
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        _scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
    }

    [Fact]
    public async Task GetResponseAsync_RecordsUsageWhenExecutionIdIsSet()
    {
        var executionId = Guid.NewGuid();
        var innerClient = new Mock<IChatClient>();
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, "hello")])
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }
        };
        innerClient.Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var decorator = new TokenTrackingDecorator(innerClient.Object, _scopeFactory.Object, "claude-test");

        AgentExecutionContext.CurrentExecutionId = executionId;
        try
        {
            await decorator.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

            _tokenTracker.Verify(t => t.RecordUsageAsync(
                executionId, "claude-test", 100, 50, 0, 0,
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            AgentExecutionContext.CurrentExecutionId = null;
        }
    }

    [Fact]
    public async Task GetResponseAsync_SkipsRecordingWhenNoExecutionId()
    {
        var innerClient = new Mock<IChatClient>();
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, "hello")])
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }
        };
        innerClient.Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var decorator = new TokenTrackingDecorator(innerClient.Object, _scopeFactory.Object, "claude-test");

        AgentExecutionContext.CurrentExecutionId = null;
        await decorator.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        _tokenTracker.Verify(t => t.RecordUsageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetResponseAsync_DoesNotThrowWhenTrackerFails()
    {
        var executionId = Guid.NewGuid();
        var innerClient = new Mock<IChatClient>();
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, "hello")])
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }
        };
        innerClient.Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        _tokenTracker.Setup(t => t.RecordUsageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var decorator = new TokenTrackingDecorator(innerClient.Object, _scopeFactory.Object, "claude-test");

        AgentExecutionContext.CurrentExecutionId = executionId;
        try
        {
            var result = await decorator.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

            // Should still return the response despite tracker failure
            Assert.NotNull(result);
            Assert.Equal("hello", result.Messages[0].Text);
        }
        finally
        {
            AgentExecutionContext.CurrentExecutionId = null;
        }
    }
}
