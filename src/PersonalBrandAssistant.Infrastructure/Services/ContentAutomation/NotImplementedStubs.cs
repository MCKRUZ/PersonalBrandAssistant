using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentAutomation;

internal sealed class DailyContentOrchestratorStub : IDailyContentOrchestrator
{
    public Task<AutomationRunResult> ExecuteAsync(ContentAutomationOptions options, CancellationToken ct)
        => throw new NotImplementedException("Replaced in section-08");
}

internal sealed class ComfyUiClientStub : IComfyUiClient
{
    public Task<string> QueuePromptAsync(JsonObject workflow, CancellationToken ct)
        => throw new NotImplementedException("Replaced in section-02");

    public Task<ComfyUiResult> WaitForCompletionAsync(string promptId, CancellationToken ct)
        => throw new NotImplementedException("Replaced in section-02");

    public Task<byte[]> DownloadImageAsync(string filename, string subfolder, CancellationToken ct)
        => throw new NotImplementedException("Replaced in section-02");

    public Task<bool> IsAvailableAsync(CancellationToken ct)
        => throw new NotImplementedException("Replaced in section-02");
}

internal sealed class ImageGenerationServiceStub : IImageGenerationService
{
    public Task<ImageGenerationResult> GenerateAsync(string prompt, ImageGenerationOptions options, CancellationToken ct)
        => throw new NotImplementedException("Replaced in section-03");
}

internal sealed class ImagePromptServiceStub : IImagePromptService
{
    public Task<string> GeneratePromptAsync(string postContent, CancellationToken ct)
        => throw new NotImplementedException("Replaced in section-03");
}

internal sealed class ImageResizerStub : IImageResizer
{
    public Task<IReadOnlyDictionary<PlatformType, string>> ResizeForPlatformsAsync(
        string sourceFileId, PlatformType[] platforms, CancellationToken ct)
        => throw new NotImplementedException("Replaced in section-04");
}

internal sealed class DailyContentProcessorStub : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
