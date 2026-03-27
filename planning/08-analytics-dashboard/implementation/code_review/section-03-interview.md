# Section 03 Code Review Interview

## Auto-fixes Applied

### MED-1: SSRF scheme validation
**Action:** Auto-fix
**Change:** Added `uri.Scheme == Uri.UriSchemeHttps` check to `IsValidSubstackHost()` to reject non-HTTPS URLs.

### MED-2: OperationCanceledException handling
**Action:** Auto-fix
**Change:** Added explicit `catch (OperationCanceledException)` before generic `Exception` catch to avoid logging cancellation as an error.

### LOW-3: XmlResolver = null
**Action:** Auto-fix
**Change:** Added `XmlResolver = null` to XmlReaderSettings for explicit safety documentation.

### Additional test cases
**Action:** Auto-fix
**Change:** Added tests for HTTP scheme SSRF rejection, cancellation token propagation, and empty feed handling.

## Items Let Go

- **LOW-1:** Shared StripHtml utility — premature extraction, minimal duplication
- **LOW-2:** limit <= 0 validation — empty list is a reasonable default
- **LOW-4:** SubstackOptions init-only — defined in section-01, needs `set` for options binding
- **LOW-5:** Test HttpClient disposal — harmless in test context
