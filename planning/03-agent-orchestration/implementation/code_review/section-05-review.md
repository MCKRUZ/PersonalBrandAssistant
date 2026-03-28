# Section 05 - Prompt Template System: Code Review

**Reviewer:** code-reviewer agent
**Date:** 2026-03-13

---

## Summary

This section introduces a Liquid-based prompt template system using Fluid.Core. The implementation covers:
- PromptTemplateService with ConcurrentDictionary caching, brand voice injection, and dev-only FileSystemWatcher
- 15 Liquid template files organized by agent domain (social, writer, engagement, analytics, repurpose, shared)
- IPromptTemplateService interface in the Application layer
- 8 unit tests covering core rendering, caching, brand voice injection, and error cases

Overall the code is clean, well-structured, and follows the project conventions. The issues below are ranked by severity.

---

## CRITICAL Issues

### [CRITICAL-01] Path traversal via agentName / templateName parameters

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs:52`

The `agentName` and `templateName` parameters are concatenated directly into a file path with no validation. A caller passing `agentName = "../../etc"` or `templateName = "../../appsettings"` could read arbitrary files on disk.

```csharp
// Current (vulnerable)
var cacheKey = $"{agentName}/{templateName}";
// ... later in GetOrParseTemplate:
var filePath = Path.Combine(_promptsPath, $"{cacheKey}.liquid");
```

**Fix:** Validate that the resolved path stays within `_promptsPath`:

```csharp
private IFluidTemplate GetOrParseTemplate(string cacheKey)
{
    if (_cache.TryGetValue(cacheKey, out var cached))
    {
        _logger.LogDebug("Template cache hit: {CacheKey}", cacheKey);
        return cached;
    }

    var filePath = Path.GetFullPath(Path.Combine(_promptsPath, $"{cacheKey}.liquid"));
    var basePath = Path.GetFullPath(_promptsPath);

    if (!filePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException($"Template path escapes base directory: {cacheKey}");

    if (!File.Exists(filePath))
        throw new FileNotFoundException($"Prompt template not found: {filePath}", filePath);
    // ...
}
```

Also validate inputs at the `RenderAsync` and `ListTemplates` entry points:

```csharp
private static void ValidateSegment(string value, string paramName)
{
    if (string.IsNullOrWhiteSpace(value))
        throw new ArgumentException($"{paramName} cannot be empty.", paramName);

    if (value.Contains("..") || value.Contains(/) || value.Contains('\'))
        throw new ArgumentException($"{paramName} contains invalid characters.", paramName);
}
```

### [CRITICAL-02] FluidParser is not thread-safe but used as a shared field

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs:17`

`FluidParser.TryParse` is not documented as thread-safe. Since this service is registered as a singleton (it holds a cache and file watcher), concurrent requests hitting `GetOrParseTemplate` for different uncached keys will call `_parser.TryParse` simultaneously.

```csharp
private readonly FluidParser _parser = new();
```

**Fix:** Either use a `lock` around the parse operation, or create a new `FluidParser` per parse call (they are lightweight):

```csharp
private IFluidTemplate GetOrParseTemplate(string cacheKey)
{
    if (_cache.TryGetValue(cacheKey, out var cached))
        return cached;

    var content = File.ReadAllText(filePath);
    var parser = new FluidParser(); // new instance per call -- safe
    if (!parser.TryParse(content, out var template, out var error))
        throw new InvalidOperationException(
            $"Failed to parse template: {error}");

    _cache.TryAdd(cacheKey, template);
    return template;
}
```

Alternatively, use `_cache.GetOrAdd(cacheKey, _ => ParseTemplate(cacheKey))` to ensure single-parse semantics, though that requires care with the throwing factory.

---

## HIGH Issues

### [HIGH-01] Race condition in GetOrParseTemplate -- double parse + double file read

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs:93-111`

Two threads requesting the same uncached template simultaneously will both read the file and parse it. While `ConcurrentDictionary.TryAdd` is safe (one wins, one is discarded), the redundant file I/O and parse are wasteful and could produce subtle issues under load.

**Fix:** Use `GetOrAdd` with `Lazy<T>` to ensure single-execution semantics:

```csharp
private readonly ConcurrentDictionary<string, Lazy<IFluidTemplate>> _cache = new();

private IFluidTemplate GetOrParseTemplate(string cacheKey)
{
    var lazy = _cache.GetOrAdd(cacheKey, key => new Lazy<IFluidTemplate>(() =>
    {
        var filePath = Path.GetFullPath(
            Path.Combine(_promptsPath, $"{key}.liquid"));
        // ... validation, read, parse ...
        return template;
    }));

    return lazy.Value;
}
```

### [HIGH-02] Brand voice is rendered on every call -- even for templates that do not use it

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs:58-70`

Every `RenderAsync` call checks for the brand-voice file and renders it, even for templates that do not contain the brand_voice_block variable. This is unnecessary work and couples all templates to the shared brand-voice template.

**Fix:** Consider one of:
1. Only inject brand_voice_block for system templates (convention: only system.liquid files use it).
2. Check whether the raw template source contains the brand_voice_block token before rendering it.
3. Accept this as a design tradeoff and document it. The perf impact is small for now, but it doubles the render cost per call.

### [HIGH-03] File.Exists check in RenderAsync bypasses cache on every call

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs:60`

```csharp
if (File.Exists(brandVoicePath) || _cache.ContainsKey(brandVoiceKey))
```

This calls `File.Exists` on every render even when the template is already cached. The `||` short-circuits, so the file system is hit first every time.

**Fix:** Reverse the condition to check cache first:

```csharp
if (_cache.ContainsKey(brandVoiceKey) || File.Exists(brandVoicePath))
```

---

## MEDIUM Issues

### [MEDIUM-01] FileSystemWatcher can fire duplicate events

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs:35-44`

`FileSystemWatcher` is notorious for firing multiple Changed events for a single file save (editors often write-then-rename). The current handler calls `TryRemove` which is idempotent, so this is safe, but it will produce noisy log output.

**Suggestion:** Add a small debounce or suppress duplicate log entries. Not urgent since this is dev-only.

### [MEDIUM-02] Synchronous file I/O in an async method

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs:105`

```csharp
var content = File.ReadAllText(filePath);
```

This blocks a thread during file read. For a template service called from async request pipelines, prefer `File.ReadAllTextAsync`. This would require making `GetOrParseTemplate` async, which is straightforward since `RenderAsync` is already async.

### [MEDIUM-03] Interface uses concrete Dictionary instead of abstraction

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IPromptTemplateService.cs:5`

**Fix:** Use `IReadOnlyDictionary<string, object>` for the interface contract. This communicates intent (the service reads but does not mutate the dictionary) and allows callers to pass frozen/immutable dictionaries.

### [MEDIUM-04] No cancellation token support

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IPromptTemplateService.cs:5`

Async methods in request pipelines should accept `CancellationToken` to support request cancellation. Add `CancellationToken cancellationToken = default` as a parameter to `RenderAsync`.

### [MEDIUM-05] ContentPromptModel.Metadata is mutable despite being a record

**File:** `src/PersonalBrandAssistant.Application/Common/Models/ContentPromptModel.cs:12`

```csharp
public Dictionary<string, string> Metadata { get; init; } = new();
```

Per project coding style rules (immutability is critical), this should be `IReadOnlyDictionary<string, string>`.

---

## SUGGESTIONS

### [SUGGEST-01] Missing tests

Current tests (8) cover the happy paths well. Missing coverage:
- **Path traversal attempt** -- assert that ../ in agentName throws (once CRITICAL-01 is fixed)
- **Malformed Liquid template** -- assert InvalidOperationException on parse failure
- **FileSystemWatcher eviction** -- test in Development environment that changing a file invalidates cache
- **Concurrent rendering** -- verify thread safety under parallel calls
- **Empty variables dictionary** -- verify templates with no variables render cleanly
- **ListTemplates does not leak non-liquid files** -- put a .txt file alongside .liquid files

### [SUGGEST-02] Consider making PromptTemplateService implement IAsyncDisposable

The FileSystemWatcher disposal is synchronous which is fine, but if future resources need async cleanup, having IAsyncDisposable in place prevents a breaking change.

### [SUGGEST-03] Template files -- consider adding comment headers

The 15 Liquid templates are clear but would benefit from a brief header comment documenting expected variables. This makes the template contract explicit without needing to read the C# code.

### [SUGGEST-04] TemplateOptions -- register additional model types proactively

Only BrandProfilePromptModel and ContentPromptModel are registered with Fluid MemberAccessStrategy. If templates access nested objects from Dictionary<string, object> variables (like task.platform), Fluid default MemberAccessStrategy may not resolve them unless they are registered or the strategy is set to UnsafeMemberAccessStrategy. Verify that anonymous/dynamic objects used as "task" are accessible at runtime.

---

## Template File Quality

The 15 Liquid templates are well-organized by domain. A few minor observations:

- **Consistent structure:** All follow a clear pattern of instruction, conditional context, and requirements list. Good.
- **brand-voice.liquid** references brand.PreferredTerms.size and brand.AvoidedTerms.size -- verify Fluid resolves .size on IReadOnlyList<string>. Fluid uses .size (Liquid convention), which maps to Count on .NET collections. This should work with Fluid built-in member resolution, but add a test confirming empty-list conditional logic.
- **No XSS concern:** These templates produce LLM prompts, not HTML. Template output is consumed by the AI API, not rendered in browsers. No escaping concern.

---

## Verdict

| Category | Count |
|----------|-------|
| CRITICAL | 2 |
| HIGH | 3 |
| MEDIUM | 5 |
| SUGGESTION | 4 |

**Decision: BLOCK** -- Two CRITICAL issues (path traversal, parser thread safety) must be resolved before merge. The HIGH issues should also be addressed in this pass since they are low-effort fixes that prevent real production problems.

Once CRITICAL and HIGH items are fixed, this is a clean, well-designed template system that follows the project architecture well.
