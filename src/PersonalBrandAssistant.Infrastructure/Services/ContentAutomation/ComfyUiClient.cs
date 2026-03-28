using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentAutomation;

public sealed class ComfyUiClient : IComfyUiClient
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ImageGenerationOptions _options;
    private readonly ILogger<ComfyUiClient> _logger;
    private readonly string _clientId = Guid.NewGuid().ToString();

    public ComfyUiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<ContentAutomationOptions> options,
        ILogger<ComfyUiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.ImageGeneration;
        _logger = logger;
    }

    public async Task<string> QueuePromptAsync(JsonObject workflow, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("ComfyUI");
        var body = new JsonObject
        {
            ["prompt"] = JsonNode.Parse(workflow.ToJsonString()),
            ["client_id"] = _clientId,
        };

        using var response = await client.PostAsync(
            "/prompt",
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorNode = JsonNode.Parse(responseBody);
            var errorMsg = errorNode?["error"]?.GetValue<string>() ?? $"HTTP {(int)response.StatusCode}";
            throw new InvalidOperationException($"ComfyUI prompt error: {errorMsg}");
        }

        var result = JsonNode.Parse(responseBody);
        return result?["prompt_id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("ComfyUI response missing prompt_id");
    }

    public async Task<ComfyUiResult> WaitForCompletionAsync(string promptId, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var linkedToken = timeoutCts.Token;

        try
        {
            return await WaitViaWebSocketAsync(promptId, linkedToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "WebSocket connection failed for {PromptId}, falling back to polling", promptId);
            return await PollForCompletionAsync(promptId, linkedToken);
        }
    }

    public async Task<byte[]> DownloadImageAsync(string filename, string subfolder, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("ComfyUI");
        var url = $"/view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type=output";

        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.HealthCheckTimeoutSeconds));

            var client = _httpClientFactory.CreateClient("ComfyUI");
            using var response = await client.GetAsync("/system_stats", timeoutCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<ComfyUiResult> WaitViaWebSocketAsync(string promptId, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        var wsUrl = _options.ComfyUiBaseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        await ws.ConnectAsync(new Uri($"{wsUrl}/ws?clientId={_clientId}"), ct);

        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var msg = JsonNode.Parse(json);
            var type = msg?["type"]?.GetValue<string>();

            if (type == "executing")
            {
                var data = msg?["data"];
                var node = data?["node"];
                var msgPromptId = data?["prompt_id"]?.GetValue<string>();

                if (node is null && msgPromptId == promptId)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                    return await FetchHistoryAsync(promptId, ct);
                }
            }
        }

        throw new OperationCanceledException("WebSocket wait timed out", ct);
    }

    private async Task<ComfyUiResult> PollForCompletionAsync(string promptId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var client = _httpClientFactory.CreateClient("ComfyUI");
            using var response = await client.GetAsync($"/history/{promptId}", ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var history = JsonNode.Parse(body);
                var promptHistory = history?[promptId];

                if (promptHistory?["outputs"] is JsonObject outputs && outputs.Count > 0)
                {
                    return ParseHistoryOutputs(promptId, outputs);
                }
            }

            await Task.Delay(PollInterval, ct);
        }

        throw new OperationCanceledException("Polling timed out", ct);
    }

    private async Task<ComfyUiResult> FetchHistoryAsync(string promptId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("ComfyUI");
        using var response = await client.GetAsync($"/history/{promptId}", ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var history = JsonNode.Parse(body);
        var outputs = history?[promptId]?["outputs"] as JsonObject;

        if (outputs is null || outputs.Count == 0)
        {
            return new ComfyUiResult(false, null, null, "No outputs in history");
        }

        return ParseHistoryOutputs(promptId, outputs);
    }

    private static ComfyUiResult ParseHistoryOutputs(string promptId, JsonObject outputs)
    {
        foreach (var (_, nodeOutput) in outputs)
        {
            var images = nodeOutput?["images"] as JsonArray;
            if (images is { Count: > 0 })
            {
                var first = images[0];
                var filename = first?["filename"]?.GetValue<string>();
                var subfolder = first?["subfolder"]?.GetValue<string>() ?? "";
                if (filename is not null)
                {
                    return new ComfyUiResult(true, filename, subfolder, null);
                }
            }
        }

        return new ComfyUiResult(false, null, null, $"No images in outputs for {promptId}");
    }
}
