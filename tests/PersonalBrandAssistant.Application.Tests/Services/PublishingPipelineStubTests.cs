using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Application.Tests.Services;

public class PublishingPipelineStubTests
{
    [Fact]
    public async Task PublishAsync_ReturnsFailure_WithInternalError()
    {
        var stub = new PublishingPipelineStub();

        var result = await stub.PublishAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
        Assert.Contains("Publishing pipeline not implemented", result.Errors);
    }
}
