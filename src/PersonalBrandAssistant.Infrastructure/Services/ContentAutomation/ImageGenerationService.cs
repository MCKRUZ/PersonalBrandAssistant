using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentAutomation;

public sealed class ImageGenerationService : IImageGenerationService
{
    private static readonly byte[] PngMagicBytes = [0x89, 0x50, 0x4E, 0x47];

    private readonly IComfyUiClient _comfyUiClient;
    private readonly IMediaStorage _mediaStorage;
    private readonly ImageGenerationOptions _options;
    private readonly ILogger<ImageGenerationService> _logger;

    public ImageGenerationService(
        IComfyUiClient comfyUiClient,
        IMediaStorage mediaStorage,
        IOptions<ContentAutomationOptions> options,
        ILogger<ImageGenerationService> logger)
    {
        _comfyUiClient = comfyUiClient;
        _mediaStorage = mediaStorage;
        _options = options.Value.ImageGeneration;
        _logger = logger;
    }

    public async Task<ImageGenerationResult> GenerateAsync(
        string prompt, ImageGenerationOptions options, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!await _comfyUiClient.IsAvailableAsync(ct))
            {
                return new ImageGenerationResult(false, null, "ComfyUI is unavailable", 0);
            }

            var workflow = LoadAndInjectWorkflow(prompt, options);
            var promptId = await _comfyUiClient.QueuePromptAsync(workflow, ct);

            _logger.LogInformation("ComfyUI prompt queued: {PromptId}", promptId);

            var result = await _comfyUiClient.WaitForCompletionAsync(promptId, ct);
            if (!result.Success || result.OutputFilename is null)
            {
                return new ImageGenerationResult(false, null, result.Error ?? "No output", stopwatch.ElapsedMilliseconds);
            }

            var imageBytes = await _comfyUiClient.DownloadImageAsync(
                result.OutputFilename, result.OutputSubfolder ?? "", ct);

            if (!ValidatePng(imageBytes))
            {
                _logger.LogWarning("Downloaded image failed PNG validation");
                return new ImageGenerationResult(false, null, "Downloaded image is not a valid PNG", stopwatch.ElapsedMilliseconds);
            }

            using var stream = new MemoryStream(imageBytes);
            var fileId = await _mediaStorage.SaveAsync(stream, "generated.png", "image/png", ct);

            _logger.LogInformation("Image generated and stored: {FileId} ({DurationMs}ms)", fileId, stopwatch.ElapsedMilliseconds);
            return new ImageGenerationResult(true, fileId, null, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image generation failed");
            return new ImageGenerationResult(false, null, ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    private JsonObject LoadAndInjectWorkflow(string prompt, ImageGenerationOptions options)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "PersonalBrandAssistant.Infrastructure.Services.ContentAutomation.Workflows.flux-text-to-image.json";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var workflow = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Failed to parse workflow template");

        // Inject prompt into CLIPTextEncode (positive prompt node)
        var clipNode = FindNodeByClassType(workflow, "CLIPTextEncode");
        if (clipNode is not null)
        {
            clipNode["inputs"]!["text"] = prompt;
        }

        // Inject seed into KSampler
        var samplerNode = FindNodeByClassType(workflow, "KSampler");
        if (samplerNode is not null)
        {
            samplerNode["inputs"]!["seed"] = Random.Shared.NextInt64();
        }

        // Inject dimensions into EmptyLatentImage
        var latentNode = FindNodeByClassType(workflow, "EmptyLatentImage");
        if (latentNode is not null)
        {
            latentNode["inputs"]!["width"] = options.DefaultWidth;
            latentNode["inputs"]!["height"] = options.DefaultHeight;
        }

        // Inject model checkpoint if configured
        if (!string.IsNullOrEmpty(options.ModelCheckpoint))
        {
            var unetNode = FindNodeByClassType(workflow, "UNETLoader");
            if (unetNode is not null)
            {
                unetNode["inputs"]!["unet_name"] = options.ModelCheckpoint;
            }
        }

        return workflow;
    }

    private static JsonNode? FindNodeByClassType(JsonObject workflow, string classType)
    {
        foreach (var (_, node) in workflow)
        {
            if (node?["class_type"]?.GetValue<string>() == classType)
                return node;
        }
        return null;
    }

    private static bool ValidatePng(byte[] data) =>
        data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual(PngMagicBytes);
}
