namespace PBA.Infrastructure.Configuration;

public sealed class ComfyUiOptions
{
    public const string SectionName = "ComfyUi";

    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "http://localhost:8188";

    /// <summary>Filesystem path to the user's exported API-format ComfyUI workflow JSON.</summary>
    public string WorkflowPath { get; init; } = string.Empty;

    /// <summary>Workflow node id whose <c>inputs.text</c> receives the positive prompt.</summary>
    public string PromptNodeId { get; init; } = string.Empty;

    /// <summary>Directory where the generated <c>{slug}.png</c> is written.</summary>
    public string OutputDirectory { get; init; } = string.Empty;

    public int TimeoutMs { get; init; } = 180_000;
    public int PollIntervalMs { get; init; } = 1_500;
}
