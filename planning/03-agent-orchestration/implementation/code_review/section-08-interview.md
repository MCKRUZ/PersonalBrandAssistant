# Section 08 — Code Review Interview

## Auto-Fixed

### W-01 - ex.Message leaked to callers
- Changed to generic error message: "encountered an unexpected error"
- Full exception still logged via ILogger
- **Status:** APPLIED

### W-02 - Token counts not populated
- Refactored `BuildOutput` to accept `UsageDetails?` from `ChatResponse.Usage`
- Populates `InputTokens` and `OutputTokens` on `AgentOutput`
- **Status:** APPLIED

### W-05 - Duplicate CreateBrandProfile in WriterTests
- Replaced with shared `TestBrandProfile.Create()` helper
- **Status:** APPLIED

### W-06 - No exception path test
- Added `ExecuteAsync_ReturnsFailureOnChatClientException` test
- Verifies failure result and that ex.Message is not leaked
- **Status:** APPLIED

### S-02 - Parameters override brand/content keys
- Changed to namespace parameters under `"task"` key per plan spec
- Added `ExecuteAsync_NamespacesParametersUnderTaskKey` test
- **Status:** APPLIED

## Let Go

### W-03/W-04 - Agentic loop, JSON parsing, multi-item output
- Plan describes aspirational features (agentic loop for Writer, JSON parsing for Social, multi-item for Repurpose)
- Single-call pattern is correct for v1. These can be layered on when tool/function calling is implemented.
- Will note in section documentation as planned future enhancements.

### S-01 - Template name validation
- Template path traversal already handled by PromptTemplateService (section-05)

### S-03 - CancellationToken for RenderAsync
- Would require interface change. Deferred.

### S-04 - Shared test base class
- Nice-to-have but current duplication is minimal with TestBrandProfile helper.

### S-05 - Document H1-only title regex
- Self-evident from the regex pattern. Not worth a comment.
