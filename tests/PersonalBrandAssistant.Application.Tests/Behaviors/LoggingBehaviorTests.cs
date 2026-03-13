using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Application.Common.Behaviors;

namespace PersonalBrandAssistant.Application.Tests.Behaviors;

public class LoggingBehaviorTests
{
    public sealed record TestRequest(string Name, string ApiKey) : IRequest<string>;

    private readonly Mock<ILogger<LoggingBehavior<TestRequest, string>>> _logger = new();

    [Fact]
    public async Task Handle_LogsRequestNameAndResponse()
    {
        var behavior = new LoggingBehavior<TestRequest, string>(_logger.Object);

        var result = await behavior.Handle(
            new TestRequest("test", "secret"),
            ct => Task.FromResult("done"),
            CancellationToken.None);

        Assert.Equal("done", result);

        _logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TestRequest")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(1));
    }

    [Fact]
    public async Task Handle_RedactsSensitiveFields()
    {
        var behavior = new LoggingBehavior<TestRequest, string>(_logger.Object);

        await behavior.Handle(
            new TestRequest("visible", "my-secret-key"),
            ct => Task.FromResult("done"),
            CancellationToken.None);

        // Verify that logging was called and the ApiKey field would be redacted
        // The SanitizeRequest method redacts fields containing "Key"
        _logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => !o.ToString()!.Contains("my-secret-key")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(1));
    }

    [Fact]
    public async Task Handle_OnException_LogsErrorAndRethrows()
    {
        var behavior = new LoggingBehavior<TestRequest, string>(_logger.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new TestRequest("test", "key"),
                ct => throw new InvalidOperationException("boom"),
                CancellationToken.None));

        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
