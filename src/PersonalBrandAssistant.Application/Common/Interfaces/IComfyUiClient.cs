using System.Text.Json.Nodes;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IComfyUiClient
{
    Task<string> QueuePromptAsync(JsonObject workflow, CancellationToken ct);
    Task<ComfyUiResult> WaitForCompletionAsync(string promptId, CancellationToken ct);
    Task<byte[]> DownloadImageAsync(string filename, string subfolder, CancellationToken ct);
    Task<bool> IsAvailableAsync(CancellationToken ct);
}
