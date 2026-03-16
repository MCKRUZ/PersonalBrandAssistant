using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.BackgroundJobs;

namespace PersonalBrandAssistant.Infrastructure.Tests.BackgroundJobs;

public class TrendAggregationProcessorTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<ITrendMonitor> _trendMonitor = new();
    private readonly Mock<ILogger<TrendAggregationProcessor>> _logger = new();
    private readonly TrendMonitoringOptions _options = new();

    public TrendAggregationProcessorTests()
    {
        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(ITrendMonitor)))
            .Returns(_trendMonitor.Object);
    }

    private TrendAggregationProcessor CreateSut() => new(
        _scopeFactory.Object,
        Options.Create(_options),
        _logger.Object);

    [Fact]
    public async Task ProcessCycleAsync_CallsRefreshTrendsAsync()
    {
        _trendMonitor.Setup(m => m.RefreshTrendsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        var sut = CreateSut();
        await sut.ProcessCycleAsync(CancellationToken.None);

        _trendMonitor.Verify(m => m.RefreshTrendsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessCycleAsync_HandlesRefreshFailure_DoesNotThrow()
    {
        _trendMonitor.Setup(m => m.RefreshTrendsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MediatR.Unit>.Failure(
                Application.Common.Errors.ErrorCode.ValidationFailed, "Source unavailable"));

        var sut = CreateSut();

        var exception = await Record.ExceptionAsync(
            () => sut.ProcessCycleAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ProcessCycleAsync_HandlesException_DoesNotThrow()
    {
        _trendMonitor.Setup(m => m.RefreshTrendsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var sut = CreateSut();

        var exception = await Record.ExceptionAsync(
            () => sut.ProcessCycleAsync(CancellationToken.None));

        Assert.Null(exception);
    }
}
