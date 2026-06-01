using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Publishing;

/// <summary>
/// Config-driven hero-image generator backed by a self-hosted ComfyUI instance.
/// The workflow graph is instance-specific (references the user's checkpoint/model node ids),
/// so it is loaded from <see cref="ComfyUiOptions.WorkflowPath"/> rather than hardcoded.
/// </summary>
public sealed class ComfyUiHeroImageGenerator(
    HttpClient httpClient,
    IOptionsMonitor<ComfyUiOptions> options,
    ILogger<ComfyUiHeroImageGenerator> logger) : IHeroImageGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Result<string>> GenerateAsync(BlogPostMeta post, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.WorkflowPath) || !File.Exists(opts.WorkflowPath))
            return Result<string>.Fail("ComfyUI hero image generation is not configured");

        try
        {
            var workflowJson = await File.ReadAllTextAsync(opts.WorkflowPath, ct);
            var prompt = BuildPrompt(post);
            var injected = InjectPrompt(workflowJson, prompt, opts.PromptNodeId);

            var promptId = await SubmitPromptAsync(opts, injected, ct);
            if (promptId is null)
                return Result<string>.Fail("ComfyUI did not return a prompt_id");

            var image = await PollForImageAsync(opts, promptId, ct);
            if (image is null)
                return Result<string>.Fail($"ComfyUI did not produce an image within {opts.TimeoutMs}ms");

            var bytes = await DownloadImageAsync(opts, image.Value, ct);
            if (bytes is null || bytes.Length == 0)
                return Result<string>.Fail("ComfyUI image download returned no bytes");

            Directory.CreateDirectory(opts.OutputDirectory);
            var path = Path.Combine(opts.OutputDirectory, $"{post.Slug}.png");
            await File.WriteAllBytesAsync(path, bytes, ct);

            logger.LogInformation("Generated hero image for {Slug} at {Path}", post.Slug, path);
            return Result<string>.Success(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ComfyUI hero image generation failed for {Slug}", post.Slug);
            return Result<string>.Fail($"ComfyUI hero image generation failed: {ex.Message}");
        }
    }

    internal static string BuildPrompt(BlogPostMeta post)
    {
        // Bias toward the site's dark/gold editorial aesthetic (bg ~#0f1117, accent ~#d4a853).
        return
            $"Editorial hero illustration for an enterprise AI thought-leadership article titled \"{post.Title}\". " +
            $"Theme: {post.Category}. " +
            "Dark moody background (deep charcoal #0f1117), elegant gold accents (#d4a853), " +
            "premium magazine cover-art style, abstract technological motifs, " +
            "cinematic lighting, high contrast, clean negative space, no text, 16:9 widescreen.";
    }

    internal static string InjectPrompt(string workflowJson, string prompt, string promptNodeId)
    {
        var root = JsonNode.Parse(workflowJson)
            ?? throw new InvalidOperationException("Workflow JSON is empty or invalid");

        var node = root[promptNodeId]
            ?? throw new InvalidOperationException($"Workflow has no node with id '{promptNodeId}'");

        var inputs = node["inputs"]
            ?? throw new InvalidOperationException($"Node '{promptNodeId}' has no 'inputs' object");

        inputs["text"] = prompt;

        return root.ToJsonString();
    }

    private async Task<string?> SubmitPromptAsync(ComfyUiOptions opts, string workflowJson, CancellationToken ct)
    {
        var payload = $"{{\"prompt\":{workflowJson}}}";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{opts.BaseUrl.TrimEnd('/')}/prompt")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("ComfyUI /prompt failed: {Status} {Body}", response.StatusCode, body);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var parsed = JsonSerializer.Deserialize<ComfyPromptResponse>(json, JsonOptions);
        return parsed?.PromptId;
    }

    private async Task<ComfyImageRef?> PollForImageAsync(ComfyUiOptions opts, string promptId, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < opts.TimeoutMs)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{opts.BaseUrl.TrimEnd('/')}/history/{promptId}");

            var response = await httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var image = ExtractFirstImage(json, promptId);
                if (image is not null)
                    return image;
            }

            await Task.Delay(opts.PollIntervalMs, ct);
        }

        return null;
    }

    private static ComfyImageRef? ExtractFirstImage(string historyJson, string promptId)
    {
        using var doc = JsonDocument.Parse(historyJson);

        if (!doc.RootElement.TryGetProperty(promptId, out var entry))
            return null;

        if (!entry.TryGetProperty("outputs", out var outputs))
            return null;

        foreach (var node in outputs.EnumerateObject())
        {
            if (!node.Value.TryGetProperty("images", out var images))
                continue;

            foreach (var img in images.EnumerateArray())
            {
                var filename = img.TryGetProperty("filename", out var f) ? f.GetString() : null;
                if (string.IsNullOrEmpty(filename))
                    continue;

                var subfolder = img.TryGetProperty("subfolder", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                var type = img.TryGetProperty("type", out var t) ? t.GetString() ?? "output" : "output";
                return new ComfyImageRef(filename, subfolder, type);
            }
        }

        return null;
    }

    private async Task<byte[]?> DownloadImageAsync(ComfyUiOptions opts, ComfyImageRef image, CancellationToken ct)
    {
        var url =
            $"{opts.BaseUrl.TrimEnd('/')}/view?filename={Uri.EscapeDataString(image.Filename)}" +
            $"&subfolder={Uri.EscapeDataString(image.Subfolder)}" +
            $"&type={Uri.EscapeDataString(image.Type)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("ComfyUI /view failed: {Status}", response.StatusCode);
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private readonly record struct ComfyImageRef(string Filename, string Subfolder, string Type);

    private sealed class ComfyPromptResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("prompt_id")]
        public string? PromptId { get; init; }
    }
}
