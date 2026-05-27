using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Api.Hubs;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Api.Tests.Hubs;

public class ContentHubTests
{
    private readonly Mock<IAppDbContext> _dbMock = new();
    private readonly Mock<ISidecarClient> _sidecarMock = new();
    private readonly Mock<IHubCallerClients<IContentHubClient>> _clientsMock = new();
    private readonly Mock<IContentHubClient> _callerMock = new();
    private readonly Mock<HubCallerContext> _contextMock = new();
    private readonly ContentHub _sut;

    private readonly Content _testContent = new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Post",
        Body = "Some content body",
        ContentType = ContentType.Blog,
        PrimaryPlatform = Platform.Blog,
    };

    private readonly BrandProfile _testProfile = new()
    {
        Personality = "Thoughtful, direct",
        Tone = "Professional but approachable",
        Vocabulary = ["enterprise", "agent-first"],
        AvoidWords = ["synergy", "leverage"],
    };

    public ContentHubTests()
    {
        _clientsMock.Setup(c => c.Caller).Returns(_callerMock.Object);
        _contextMock.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);

        _sut = new ContentHub(
            _dbMock.Object,
            _sidecarMock.Object,
            NullLogger<ContentHub>.Instance);

        // Wire up Hub internals via reflection-free approach
        SetHubContext(_sut, _clientsMock.Object, _contextMock.Object);
    }

    [Fact]
    public async Task SendChatMessage_ContentNotFound_CallsGenerationError()
    {
        _dbMock.Setup(d => d.Contents.FindAsync(It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Content?)null);

        await _sut.SendChatMessage(Guid.NewGuid(), "hello");

        _callerMock.Verify(c => c.GenerationError("Content not found"), Times.Once);
        _callerMock.Verify(c => c.ReceiveToken(It.IsAny<string>()), Times.Never);
        _callerMock.Verify(c => c.GenerationComplete(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendChatMessage_CallsStreamPromptAsync()
    {
        SetupDbWithContentAndProfile();
        SetupStreamReturning(["token1", "token2"]);

        await _sut.SendChatMessage(_testContent.Id, "help me write");

        _sidecarMock.Verify(s => s.StreamPromptAsync(
            _testContent.Id,
            It.Is<string>(p => p.Contains("Thoughtful")),
            It.Is<string>(p => p.Contains("help me write") && p.Contains("Some content body")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendChatMessage_ForwardsTokensToCallerViaReceiveToken()
    {
        SetupDbWithContentAndProfile();
        SetupStreamReturning(["Hello", " world", "!"]);

        var receivedTokens = new List<string>();
        _callerMock.Setup(c => c.ReceiveToken(It.IsAny<string>()))
            .Callback<string>(t => receivedTokens.Add(t))
            .Returns(Task.CompletedTask);

        await _sut.SendChatMessage(_testContent.Id, "test");

        Assert.Equal(["Hello", " world", "!"], receivedTokens);
    }

    [Fact]
    public async Task SendChatMessage_CallsGenerationCompleteWithFullText()
    {
        SetupDbWithContentAndProfile();
        SetupStreamReturning(["Hello", " world", "!"]);

        await _sut.SendChatMessage(_testContent.Id, "test");

        _callerMock.Verify(c => c.GenerationComplete("Hello world!"), Times.Once);
    }

    [Fact]
    public async Task SendChatMessage_CallsGenerationErrorOnSidecarFailure()
    {
        SetupDbWithContentAndProfile();
        _sidecarMock.Setup(s => s.StreamPromptAsync(
                It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable("Sidecar CLI failed"));

        await _sut.SendChatMessage(_testContent.Id, "test");

        _callerMock.Verify(c => c.GenerationError("An error occurred during generation"), Times.Once);
        _callerMock.Verify(c => c.GenerationComplete(It.IsAny<string>()), Times.Never);
    }

    private void SetupDbWithContentAndProfile()
    {
        _dbMock.Setup(d => d.Contents.FindAsync(It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testContent);

        var profiles = new List<BrandProfile> { _testProfile }.AsQueryable();
        var mockSet = new Mock<DbSet<BrandProfile>>();

        // Setup IQueryable
        mockSet.As<IQueryable<BrandProfile>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<BrandProfile>(profiles.Provider));
        mockSet.As<IQueryable<BrandProfile>>().Setup(m => m.Expression).Returns(profiles.Expression);
        mockSet.As<IQueryable<BrandProfile>>().Setup(m => m.ElementType).Returns(profiles.ElementType);
        mockSet.As<IQueryable<BrandProfile>>().Setup(m => m.GetEnumerator()).Returns(profiles.GetEnumerator());
        mockSet.As<IAsyncEnumerable<BrandProfile>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<BrandProfile>(profiles.GetEnumerator()));

        _dbMock.Setup(d => d.BrandProfiles).Returns(mockSet.Object);
    }

    private void SetupStreamReturning(string[] tokens)
    {
        _sidecarMock.Setup(s => s.StreamPromptAsync(
                It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(tokens));
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(
        string[] items,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<string> ThrowingAsyncEnumerable(
        string errorMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return await Task.FromException<string>(new InvalidOperationException(errorMessage));
    }

    private static void SetHubContext(
        ContentHub hub,
        IHubCallerClients<IContentHubClient> clients,
        HubCallerContext context)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly;

        typeof(Hub<IContentHubClient>).GetProperty("Clients", flags)!.SetValue(hub, clients);
        typeof(Hub).GetProperty("Context", flags)!.SetValue(hub, context);
    }
}

internal class TestAsyncQueryProvider<T>(IQueryProvider inner) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(System.Linq.Expressions.Expression expression)
        => new TestAsyncEnumerable<T>(expression, inner);

    public IQueryable<TElement> CreateQuery<TElement>(System.Linq.Expressions.Expression expression)
        => new TestAsyncEnumerable<TElement>(expression, inner);

    public object? Execute(System.Linq.Expressions.Expression expression)
        => inner.Execute(expression);

    public TResult Execute<TResult>(System.Linq.Expressions.Expression expression)
        => inner.Execute<TResult>(expression);

    public TResult ExecuteAsync<TResult>(System.Linq.Expressions.Expression expression, CancellationToken ct = default)
    {
        var resultType = typeof(TResult).GetGenericArguments().FirstOrDefault() ?? typeof(TResult);
        var result = inner.Execute(expression);

        var taskFromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(resultType)
            .Invoke(null, [result]);

        return (TResult)taskFromResult!;
    }
}

internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    private readonly IQueryProvider _provider;

    public TestAsyncEnumerable(System.Linq.Expressions.Expression expression, IQueryProvider provider)
        : base(expression)
    {
        _provider = new TestAsyncQueryProvider<T>(provider);
    }

    IQueryProvider IQueryable.Provider => _provider;

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default)
        => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
}

internal class TestAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;
    public ValueTask<bool> MoveNextAsync() => new(inner.MoveNext());
    public ValueTask DisposeAsync()
    {
        inner.Dispose();
        return ValueTask.CompletedTask;
    }
}
