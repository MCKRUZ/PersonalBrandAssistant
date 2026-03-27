Now I have all the context needed. Let me write the section.

# Section 03: Image Services (ImageGenerationService + ImagePromptService)

## Overview

This section implements two services that form the image creation pipeline for the autonomous content workflow:

1. **ImageGenerationService** -- orchestrates the full image generation flow: health check, workflow template injection, ComfyUI submission, download, and media storage.
2. **ImagePromptService** -- uses the Claude sidecar to generate FLUX-optimized image prompts from post content.

Both services live in `Infrastructure/Services/ContentAutomation/` and implement interfaces defined in `Application/Common/Interfaces/`.

## Dependencies

**Must be completed first:**
- **section-01-foundation** -- provides `IImageGenerationService`, `IImagePromptService` interfaces, `ImageGenerationResult` model, `ComfyUiOptions`, `ContentAutomationOptions`, DI registration stubs
- **section-02-comfyui-client** -- provides `IComfyUiClient` and `ComfyUiClient` implementation (HTTP/WebSocket communication with ComfyUI)

**Existing services used (no modifications needed):**
- `IMediaStorage` at `src/PersonalBrandAssistant.Application/Common/Interfaces/IMediaStorage.cs` -- `SaveAsync(Stream, fileName, mimeType, ct)` returns a fileId string
- `ISidecarClient` at `src/PersonalBrandAssistant.Application/Common/Interfaces/ISidecarClient.cs` -- `SendTaskAsync(task, systemPrompt, sessionId, ct)` returns `IAsyncEnumerable<SidecarEvent>`
- Sidecar event types: `ChatEvent`, `TaskCompleteEvent`, `ErrorEvent` at `src/PersonalBrandAssistant.Application/Common/Models/SidecarEvent.cs`

## File Paths

### New Files to Create

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/ImageGenerationService.cs` | ComfyUI orchestration: template loading, parameter injection, queue/wait/download/store |
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/ImagePromptService.cs` | Sidecar integration for FLUX-optimized prompt generation |
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/Workflows/flux-text-to-image.json` | Embedded resource: FLUX workflow template in ComfyUI API format |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/ImageGenerationServiceTests.cs` | Unit tests for ImageGenerationService |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/ImagePromptServiceTests.cs` | Unit tests for ImagePromptService |

### Files to Modify

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj` | Add `<EmbeddedResource>` for workflow JSON template |

## Tests First

All tests use xUnit + Moq, matching existing PBA conventions.

### ImageGenerationServiceTests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/ImageGenerationServiceTests.cs`

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentAutomation;

public class ImageGenerationServiceTests
{
    // Mocked dependencies
    private readonly Mock<IComfyUiClient> _comfyUiClient;
    private readonly Mock<IMediaStorage> _mediaStorage;
    private readonly Mock<ILogger<ImageGenerationService>> _logger;
    private readonly IOptions<ComfyUiOptions> _options;
    private readonly ImageGenerationService _sut;

    // Constructor: wire up mocks, create SUT with default ComfyUiOptions

    [Fact]
    public async Task GenerateAsync_CallsIsAvailableAsync_BeforeQueueing()
    { /* Verify IsAvailableAsync is called first. If it returns false, QueuePromptAsync should never be called. */ }

    [Fact]
    public async Task GenerateAsync_ReturnsError_WhenHealthCheckFails()
    { /* IsAvailableAsync returns false -> result.Success is false, result.Error contains "unavailable" */ }

    [Fact]
    public async Task GenerateAsync_LoadsWorkflowTemplate_InjectsPromptSeedWidthHeight()
    { /* Capture the JsonObject passed to QueuePromptAsync. Verify prompt text, seed (non-zero), width, height are injected into correct nodes. */ }

    [Fact]
    public async Task GenerateAsync_CallsQueuePromptAsync_WithInjectedWorkflow()
    { /* Verify QueuePromptAsync called exactly once with a non-null workflow */ }

    [Fact]
    public async Task GenerateAsync_CallsWaitForCompletionAsync_WithReturnedPromptId()
    { /* QueuePromptAsync returns "prompt-123" -> WaitForCompletionAsync called with "prompt-123" */ }

    [Fact]
    public async Task GenerateAsync_ExtractsOutputFilename_FromCompletionResult()
    { /* ComfyUiResult has Outputs with filename -> DownloadImageAsync called with that filename */ }

    [Fact]
    public async Task GenerateAsync_DownloadsImage_ViaDownloadImageAsync()
    { /* Verify DownloadImageAsync called with correct filename and subfolder */ }

    [Fact]
    public async Task GenerateAsync_StoresImage_ViaMediaStorage()
    { /* Verify IMediaStorage.SaveAsync called with stream, "generated.png", "image/png" */ }

    [Fact]
    public async Task GenerateAsync_ReturnsSuccess_WithFileId()
    { /* Full happy path: returns ImageGenerationResult(Success: true, FileId: "stored-id", ...) */ }

    [Fact]
    public async Task GenerateAsync_ReturnsError_WhenComfyUiFails()
    { /* QueuePromptAsync throws -> result.Success is false, error message captured */ }

    [Fact]
    public async Task GenerateAsync_ValidatesPngMagicBytes_OnDownloadedImage()
    { /* Downloaded bytes start with 0x89504E47 -> passes. Invalid bytes -> returns error. */ }

    [Fact]
    public async Task GenerateAsync_ReturnsError_OnCorruptedOutput()
    { /* Downloaded bytes don't have PNG magic bytes -> result.Success is false */ }

    [Fact]
    public async Task GenerateAsync_RecordsDurationMs_InResult()
    { /* DurationMs in result should be > 0 */ }
}
```

### ImagePromptServiceTests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/ImagePromptServiceTests.cs`

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentAutomation;

public class ImagePromptServiceTests
{
    // Mocked dependencies
    private readonly Mock<ISidecarClient> _sidecarClient;
    private readonly Mock<ILogger<ImagePromptService>> _logger;
    private readonly ImagePromptService _sut;

    // Constructor: wire up mocks, create SUT

    [Fact]
    public async Task GeneratePromptAsync_SendsPostContent_ToSidecar()
    { /* Verify SendTaskAsync called with task containing the post content */ }

    [Fact]
    public async Task GeneratePromptAsync_UsesFluxOptimizedSystemPrompt()
    { /* Capture the systemPrompt argument. Verify it contains FLUX-specific instructions. */ }

    [Fact]
    public async Task GeneratePromptAsync_SystemPromptIncludesStyleKeywords()
    { /* systemPrompt contains: "minimalist", "editorial", "gradient", "professional corporate" */ }

    [Fact]
    public async Task GeneratePromptAsync_SystemPromptInstructsToAvoidProblematicContent()
    { /* systemPrompt instructs avoiding: text in image, busy compositions, photorealistic faces */ }

    [Fact]
    public async Task GeneratePromptAsync_ReturnsPromptText_FromSidecarResponse()
    { /* Sidecar yields ChatEvent with prompt text -> returns that text */ }

    [Fact]
    public async Task GeneratePromptAsync_HandlesErrorEvent_ReturnsError()
    { /* Sidecar yields ErrorEvent -> throws or returns error (does not crash) */ }

    [Fact]
    public async Task GeneratePromptAsync_HandlesTimeout_Gracefully()
    { /* CancellationToken fires after 60s -> handles gracefully without unobserved exception */ }

    [Fact]
    public async Task GeneratePromptAsync_ConcatenatesMultipleChatEvents()
    { /* Multiple ChatEvent yields -> concatenated into single prompt string */ }
}
```

## Implementation Details

### ImageGenerationService

**Namespace:** `PersonalBrandAssistant.Infrastructure.Services.ContentAutomation`

**Constructor dependencies:**
- `IComfyUiClient comfyUiClient` -- from section-02
- `IMediaStorage mediaStorage` -- existing service
- `IOptions<ComfyUiOptions> options` -- from section-01
- `ILogger<ImageGenerationService> logger`

**DI lifetime:** Scoped (registered in section-01 foundation)

**Single method:** `Task<ImageGenerationResult> GenerateAsync(string prompt, ImageGenerationOptions options, CancellationToken ct)`

**`ImageGenerationOptions`** should be a record (defined in section-01 foundation) with:
- `int Width` (default 1536)
- `int Height` (default 1536)
- `string? ModelCheckpoint` (override from config)

**Flow:**

1. **Health check** -- Call `_comfyUiClient.IsAvailableAsync(ct)`. If false, return `new ImageGenerationResult(false, null, "ComfyUI is unavailable", 0)` immediately.

2. **Load workflow template** -- Read the embedded resource `flux-text-to-image.json` via `Assembly.GetExecutingAssembly().GetManifestResourceStream()`. Deserialize into `System.Text.Json.Nodes.JsonObject`.

3. **Inject parameters** -- Navigate the workflow's node graph (numeric string keys like `"6"`, `"3"`, etc.) and set:
   - CLIPTextEncode node's `inputs.text` = the `prompt` parameter
   - KSampler node's `inputs.seed` = `Random.Shared.NextInt64()`
   - EmptyLatentImage node's `inputs.width` = `options.Width`
   - EmptyLatentImage node's `inputs.height` = `options.Height`
   - UNETLoader node's `inputs.unet_name` = `_comfyUiOptions.ModelCheckpoint` (if configured)
   
   The specific node IDs depend on the workflow template. Use constants or a lookup by `class_type` to find the correct nodes rather than hardcoding node IDs.

4. **Queue** -- `var promptId = await _comfyUiClient.QueuePromptAsync(workflow, ct)`

5. **Wait** -- `var result = await _comfyUiClient.WaitForCompletionAsync(promptId, ct)` with a `CancellationTokenSource` linked to the caller's token and a timeout of `_comfyUiOptions.TimeoutSeconds`.

6. **Extract filename** -- The `ComfyUiResult` (from section-02) contains output image info. Extract the filename and subfolder from the outputs.

7. **Download** -- `var imageBytes = await _comfyUiClient.DownloadImageAsync(filename, subfolder, ct)`

8. **Validate PNG** -- Check that `imageBytes` starts with PNG magic bytes: `0x89, 0x50, 0x4E, 0x47`. If not, return error result.

9. **Store** -- Convert bytes to `MemoryStream`, call `var fileId = await _mediaStorage.SaveAsync(stream, "generated.png", "image/png", ct)`

10. **Return** -- `new ImageGenerationResult(true, fileId, null, stopwatch.ElapsedMilliseconds)`

**Error handling:** Wrap steps 2-9 in try/catch. On any exception, log the error and return `ImageGenerationResult(false, null, ex.Message, elapsed)`. Do not rethrow -- the orchestrator (section-08) decides whether to abort the pipeline.

### Workflow Template (Embedded Resource)

File: `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/Workflows/flux-text-to-image.json`

This is a ComfyUI API-format workflow JSON exported via "Save (API Format)" from the ComfyUI UI. The template defines the full FLUX text-to-image node graph:

- **UNETLoader** -- loads the FLUX model checkpoint
- **DualCLIPLoader** -- loads CLIP text encoders (clip_l + t5xxl)
- **CLIPTextEncode** -- the positive prompt node (this is where `prompt` gets injected)
- **EmptyLatentImage** -- defines output dimensions (width/height injected here)
- **KSampler** -- sampling parameters including seed (injected), steps, cfg, sampler_name, scheduler
- **VAEDecode** -- decodes latent to pixel space
- **VAELoader** -- loads the VAE model
- **SaveImage** -- outputs the final image

The template should use reasonable defaults for FLUX: `steps: 20`, `cfg: 1.0`, `sampler_name: "euler"`, `scheduler: "simple"`.

**Embedding the resource:** Add to the `.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Services\ContentAutomation\Workflows\flux-text-to-image.json" />
</ItemGroup>
```

The resource name will be `PersonalBrandAssistant.Infrastructure.Services.ContentAutomation.Workflows.flux-text-to-image.json`.

**Node lookup strategy:** Rather than hardcoding node IDs (which are fragile), iterate the workflow's top-level keys and match by `class_type`:

```csharp
private static JsonNode? FindNodeByClassType(JsonObject workflow, string classType)
{
    foreach (var (_, node) in workflow)
    {
        if (node?["class_type"]?.GetValue<string>() == classType)
            return node;
    }
    return null;
}
```

This makes the template swappable without code changes.

### ImagePromptService

**Namespace:** `PersonalBrandAssistant.Infrastructure.Services.ContentAutomation`

**Constructor dependencies:**
- `ISidecarClient sidecarClient` -- existing singleton
- `ILogger<ImagePromptService> logger`

**DI lifetime:** Scoped (registered in section-01 foundation)

**Single method:** `Task<string> GeneratePromptAsync(string postContent, CancellationToken ct)`

**System prompt design:**

The system prompt instructs Claude to generate a FLUX-compatible image description. Key elements:

- **Role:** "You are an AI image prompt engineer specializing in FLUX text-to-image generation."
- **Input context:** The post content is provided as context for understanding the visual concept.
- **Visual direction:** Target professional, LinkedIn-appropriate visuals. Clean minimalist compositions, muted corporate palettes, editorial quality.
- **Avoid list:** Text/words in the image, busy or cluttered compositions, photorealistic human faces, neon or over-saturated colors, stock photo cliches.
- **Required style keywords:** Include `minimalist`, `flat design`, `editorial style`, `gradient background`, `high contrast`, `professional corporate` where appropriate.
- **Length constraint:** Keep the prompt under 200 words. FLUX handles ~500 tokens but shorter prompts produce more focused results.
- **Output format:** Return ONLY the image prompt text. No explanations, no markdown, no preamble.

**Flow:**

1. Build the task string: embed the post content so the sidecar has context.
2. Call `_sidecarClient.SendTaskAsync(task, systemPrompt, null, ct)`.
3. Iterate the `IAsyncEnumerable<SidecarEvent>`:
   - On `ChatEvent` where `Text` is not null: append to a `StringBuilder`.
   - On `ErrorEvent`: log the error, throw `InvalidOperationException` with the error message.
   - On `TaskCompleteEvent`: break out of iteration.
   - Ignore other event types.
4. Return the accumulated text, trimmed.

**Error handling:**
- If the sidecar yields an `ErrorEvent`, log it and throw. The caller (orchestrator) handles the exception.
- If cancellation fires (timeout), the `IAsyncEnumerable` will throw `OperationCanceledException` naturally. Let it propagate.
- If the accumulated text is empty after iteration, throw `InvalidOperationException("Sidecar returned empty image prompt")`.

### ImageGenerationResult Model

Defined in section-01 foundation. For reference, the expected shape:

```csharp
record ImageGenerationResult(bool Success, string? FileId, string? Error, long DurationMs);
```

### ComfyUiResult Model

Defined in section-02. For reference, the expected shape contains output image info (filenames, subfolder) from the ComfyUI `/history` endpoint response.

## Implementation Notes

- **Immutability:** Both services are stateless. The workflow template is loaded fresh each call (it is small and embedded). If performance profiling shows this as a bottleneck, cache the parsed `JsonObject` in a `Lazy<JsonObject>` field, but clone before mutation.

- **No console.log equivalent:** All logging through `ILogger<T>`. Use `LogInformation` for start/complete, `LogWarning` for validation failures (corrupt PNG), `LogError` for exceptions.

- **PNG magic bytes constant:** Define as `private static readonly byte[] PngMagicBytes = [0x89, 0x50, 0x4E, 0x47];` for the validation check.

- **Sidecar session management:** `SendTaskAsync` accepts `sessionId: null`, which creates a new session per call. This is correct for image prompt generation -- each prompt is independent.

- **The `ImagePromptService` does NOT generate alt text.** Alt text generation is handled separately at the orchestrator level (section-08) by including it in the same sidecar prompt or a follow-up prompt. The image prompt service focuses solely on the FLUX image generation prompt.

## Verification Checklist

After implementation, verify:
1. All 21 tests (13 for ImageGenerationService, 8 for ImagePromptService) pass with `dotnet test`
2. The embedded resource is accessible at runtime (check the resource name matches the assembly namespace path)
3. `FindNodeByClassType` correctly navigates a real ComfyUI API-format workflow JSON
4. PNG magic byte validation correctly rejects non-PNG data and accepts valid PNG headers
5. The sidecar system prompt produces prompts that are under 200 words and contain the required style keywords (manual spot check)