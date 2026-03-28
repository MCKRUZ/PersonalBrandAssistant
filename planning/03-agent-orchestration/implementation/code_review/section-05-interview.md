# Section 05 — Code Review Interview

## Auto-Fixed (no user input needed)

### CRITICAL: Path traversal vulnerability
- **Action:** Added `ValidatePathSegment()` helper rejecting `..`, `/`, `\` in agentName/templateName
- **Action:** Added `Path.GetFullPath` + `StartsWith` check inside `GetOrParseTemplate`
- **Action:** Added 3 path traversal test cases + 1 malformed template test
- **Status:** APPLIED

### CRITICAL: FluidParser thread safety
- **Action:** Changed from shared `FluidParser` field to creating new `FluidParser()` per parse call inside `GetOrParseTemplate`
- **Status:** APPLIED

### HIGH: Race condition in GetOrParseTemplate
- **Action:** Switched from `TryGetValue`/`TryAdd` pattern to `ConcurrentDictionary.GetOrAdd` with `Lazy<IFluidTemplate>` for thread-safe single initialization
- **Status:** APPLIED

### HIGH: Brand voice File.Exists check order
- **Action:** Reversed to `_cache.ContainsKey(brandVoiceKey) || File.Exists(...)` — cache check first to avoid unnecessary filesystem hit
- **Status:** APPLIED

## Let Go (deferred or not applicable)

### HIGH: Brand voice rendered unconditionally
- Acceptable overhead for now. Brand voice is cached after first render, so subsequent calls only hit in-memory template rendering.

### MEDIUM: Synchronous file I/O in async method
- `File.ReadAllText` is called once per template on first access, then cached permanently. Not worth async file I/O complexity for a one-time read.

### MEDIUM: Interface uses concrete Dictionary instead of IReadOnlyDictionary
- Interface defined in section-03, changing it here would break the contract. Can be addressed in a future refactor.

### MEDIUM: Missing CancellationToken
- Would require interface change (section-03). Deferred.

### MEDIUM: Mutable Metadata on ContentPromptModel
- Owned by section-03 record. Not in scope for this section.

### MEDIUM: FileSystemWatcher duplicate events
- Edge case in dev mode only. Not critical.
