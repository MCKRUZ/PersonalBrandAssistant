# Section 05 -- Prompt Template System

## Overview

This section implements the `PromptTemplateService` in the Infrastructure layer. It uses the **Fluid** library (Liquid template engine for .NET) to load, cache, and render prompt templates from the `prompts/` directory. The service injects brand voice into all content-generating prompts and provides variable substitution for agent-specific parameters.

**Depends on:** Section 03 (Interfaces) -- specifically `IPromptTemplateService`, `BrandProfilePromptModel`, and `ContentPromptModel` must exist before implementing this section.

**Blocks:** Section 08 (Agent Capabilities), Section 09 (Orchestrator) -- both consume `IPromptTemplateService` to assemble prompts.

---

## File Inventory

| File | Action | Project |
|------|--------|---------|
| `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs` | Create | Infrastructure |
| `prompts/shared/brand-voice.liquid` | Create | Solution root |
| `prompts/writer/system.liquid` | Create | Solution root |
| `prompts/writer/blog-post.liquid` | Create | Solution root |
| `prompts/writer/article.liquid` | Create | Solution root |
| `prompts/social/system.liquid` | Create | Solution root |
| `prompts/social/post.liquid` | Create | Solution root |
| `prompts/social/thread.liquid` | Create | Solution root |
| `prompts/repurpose/system.liquid` | Create | Solution root |
| `prompts/repurpose/blog-to-thread.liquid` | Create | Solution root |
| `prompts/repurpose/thread-to-posts.liquid` | Create | Solution root |
| `prompts/repurpose/blog-to-social.liquid` | Create | Solution root |
| `prompts/engagement/system.liquid` | Create | Solution root |
| `prompts/engagement/response-suggestion.liquid` | Create | Solution root |
| `prompts/engagement/trend-analysis.liquid` | Create | Solution root |
| `prompts/analytics/system.liquid` | Create | Solution root |
| `prompts/analytics/performance-insights.liquid` | Create | Solution root |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/PromptTemplateServiceTests.cs` | Create | Infrastructure.Tests |

---

## Tests First

All tests go in `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/PromptTemplateServiceTests.cs`. Uses xUnit and AAA pattern. The test class should create a temporary directory with sample `.liquid` files to avoid depending on real prompt files.

```csharp
// Test: RenderAsync_LoadsTemplateFromCorrectPath
//   Arrange: Create temp prompts dir with "writer/blog-post.liquid" containing "Hello {{ name }}"
//   Act: Call RenderAsync("writer", "blog-post", new Dictionary<string, object> { ["name"] = "World" })
//   Assert: Result is "Hello World"

// Test: RenderAsync_InjectsBrandVoiceBlock
//   Arrange: Create "shared/brand-voice.liquid" with brand voice text
//            Create "writer/system.liquid" containing "{{ brand_voice_block }}"
//   Act: Call RenderAsync("writer", "system", variables)
//   Assert: Result contains the brand voice text from the shared template

// Test: RenderAsync_RendersVariablesIntoTemplate
//   Arrange: Create template with "{{ brand.name }} writes about {{ brand.topics }}"
//            Pass BrandProfilePromptModel in variables dict under "brand" key
//   Act: Call RenderAsync
//   Assert: Output contains interpolated brand name and topics

// Test: RenderAsync_CachesParsedTemplates_SecondCallDoesNotReReadFile
//   Arrange: Create temp template file, call RenderAsync once
//   Act: Delete the file, call RenderAsync again with same args
//   Assert: Second call succeeds (served from cache, no file access needed)

// Test: RenderAsync_ThrowsWhenTemplateFileNotFound
//   Arrange: Empty prompts directory
//   Act/Assert: RenderAsync("writer", "nonexistent", vars) throws FileNotFoundException

// Test: ListTemplates_ReturnsAllLiquidFilesForAgent
//   Arrange: Create "writer/system.liquid", "writer/blog-post.liquid", "writer/article.liquid"
//   Act: Call ListTemplates("writer")
//   Assert: Returns ["system", "blog-post", "article"] (or equivalent names without extension)

// Test: RenderAsync_UsesPromptViewModelDTOs
//   Arrange: Create template accessing "{{ brand.name }}" and "{{ content.title }}"
//            Pass BrandProfilePromptModel and ContentPromptModel in variables
//   Act: Call RenderAsync
//   Assert: DTO properties are correctly rendered (no internal IDs or audit fields exposed)
```

---

## Implementation Details

### NuGet Package

Add `Fluid.Core` to the Infrastructure project:

```xml
<PackageReference Include="Fluid.Core" Version="2.12.1" />
```

### PromptTemplateService

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs`

**Implements:** `IPromptTemplateService` (defined in Section 03)

**Constructor dependencies:**
- `IHostEnvironment` -- to determine if running in Development (for file watcher)
- `IOptions<AgentOrchestrationOptions>` or a direct prompts path string -- the `PromptsPath` config value from `AgentOrchestration:PromptsPath` (default: `"prompts"`)
- `ILogger<PromptTemplateService>`

**Behavior:**

1. **Template Loading:** On `RenderAsync("writer", "blog-post", variables)`, resolve path as `{PromptsPath}/writer/blog-post.liquid`. Read the file content, parse it with `FluidParser.TryParse()`.

2. **Brand Voice Injection:** Before rendering any template, load `{PromptsPath}/shared/brand-voice.liquid` and render it. Make the rendered brand voice available to all templates as the `brand_voice_block` variable. This is injected into the `TemplateContext` so templates can reference `{{ brand_voice_block }}`.

3. **Template Caching:** Use a `ConcurrentDictionary<string, IFluidTemplate>` keyed by relative path (e.g., `"writer/blog-post"`). On first access, parse and cache. Subsequent calls return the cached parsed template.

4. **File Watcher (Development only):** When `IHostEnvironment.IsDevelopment()` is true, set up a `FileSystemWatcher` on the prompts directory. On file change/create/delete, evict the corresponding key from the cache dictionary. In Production, no watcher -- templates are loaded once and cached permanently (deploy new prompts by restarting the service).

5. **Variable Mapping:** The `variables` dictionary maps string keys to objects. The Fluid `TemplateContext` is populated with these values. Prompt view model DTOs (`BrandProfilePromptModel`, `ContentPromptModel`) are registered as allowed model types via `TemplateOptions.MemberAccessStrategy.Register<T>()` so Fluid can access their properties.

6. **ListTemplates:** Enumerate `{PromptsPath}/{agentName}/*.liquid` and return filenames without extension.

**Error handling:**
- Missing template file: throw `FileNotFoundException` with descriptive message including the resolved path.
- Parse failure: throw `InvalidOperationException` with Fluid parser errors.
- Log warnings for cache misses and errors. Log debug for cache hits.

---

## Liquid Template Files

All template files live under `prompts/` at the solution root. Each file should be created with meaningful starter content.

### Template Variable Contract

All templates receive variables through a `Dictionary<string, object>`:

- `brand` -- A `BrandProfilePromptModel` with properties: `Name`, `Persona`, `Tone`, `Vocabulary`, `Topics` (no internal IDs or audit fields).
- `content` -- A `ContentPromptModel` with properties: `Title`, `Body`, `Type`, `Status` (no workflow internals).
- `brand_voice_block` -- Pre-rendered string from `shared/brand-voice.liquid`.
- `task` -- A `Dictionary<string, string>` of task-specific parameters from `AgentTask.Parameters`.
- `platforms` -- Platform constraint info (character limits, formatting rules) when relevant.

### Starter Template Content

Each template file should contain a functional Liquid template skeleton. Examples of the pattern:

**`prompts/shared/brand-voice.liquid`** -- The shared brand voice partial injected into all content-generating prompts. Should reference `{{ brand.name }}`, `{{ brand.persona }}`, `{{ brand.tone }}`, and `{{ brand.topics }}`.

**`prompts/writer/system.liquid`** -- System prompt for the writer agent. Should include `{{ brand_voice_block }}` and define the writer's role and constraints.

**`prompts/writer/blog-post.liquid`** -- Task template for blog post generation. Should reference `{{ task.topic }}`, `{{ task.keywords }}`, `{{ task.target_length }}` and any content context.

**`prompts/social/post.liquid`** -- Single social post template. Should reference platform constraints and include `{{ task.platform }}`, `{{ task.tone }}`.

All other templates follow the same pattern: a system prompt per agent, and task-specific templates that accept relevant variables.

---

## Configuration

The `PromptsPath` setting is read from `appsettings.json`:

```json
{
  "AgentOrchestration": {
    "PromptsPath": "prompts"
  }
}
```

This is a relative path resolved from the application's content root. The `PromptTemplateService` should resolve the full path using `Path.Combine(environment.ContentRootPath, options.PromptsPath)`.

---

## DI Registration (handled in Section 11)

The service is registered as a **singleton** because it manages its own thread-safe cache:

```csharp
services.AddSingleton<IPromptTemplateService, PromptTemplateService>();
```

For testing in isolation, instantiate `PromptTemplateService` directly with a mock `IHostEnvironment` and temp directory path.

---

## Verification

After implementation, run:

```bash
dotnet test tests/PersonalBrandAssistant.Infrastructure.Tests --filter "PromptTemplateServiceTests"
```
