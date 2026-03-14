# External Review Integration Notes

## Review Source: OpenAI (gpt-5.2)

### Suggestions Integrated

1. **Budget race condition (#1)** — Added note about atomic budget check. For a single-user app, simple sequential check is sufficient, but we'll use a DB transaction to atomically check+reserve budget before execution starts.

2. **Retry categorization (#2)** — Updated retry strategy to only retry on transient errors (rate limits, 5xx, network). Validation/prompt errors fail immediately. Added idempotency consideration.

3. **Streaming disconnect handling (#3)** — Added finally block requirement for SSE endpoint to mark execution as Failed on client disconnect. Added Cancelled status to AgentExecutionStatus enum.

4. **AgentExecutionLog security (#4)** — Changed from "reasoning trail" to "audit log". Made prompt/content logging configurable (disabled in prod by default). Content field truncated to max 2000 chars.

5. **Conditional workflow submission (#5)** — Made workflow submission explicit via `AgentOutput.CreatesContent` flag. Only capabilities that produce Content trigger workflow transitions. EngagementAgent and AnalyticsAgent return data-only outputs.

6. **Execution timeouts (#18)** — Added per-execution timeout config and Cancelled status. CancellationTokenSource with configurable timeout wraps each execution.

7. **Template variable safety (#9)** — Added prompt view model DTOs (BrandProfilePromptModel, ContentPromptModel) instead of passing raw entities to templates.

8. **File watcher dev-only (#15)** — Template file watching enabled only in Development environment. Prod loads templates once at startup.

9. **Content creation in orchestrator (#17)** — Moved Content entity creation from capabilities to orchestrator. Capabilities return AgentOutput only; orchestrator decides whether to create Content and submit to workflow.

10. **Execute endpoint async (#21)** — Changed POST /api/agents/execute to return 202 Accepted with executionId for long-running tasks. Short tasks (Haiku-based) can optionally wait for completion.

### Suggestions NOT Integrated (with rationale)

- **Multi-tenant/per-user auth (#6, #19)** — This is a single-user personal brand tool. No multi-tenancy needed. API key auth is sufficient.
- **Prompt injection mitigations (#11)** — Valid concern but out of scope for Phase 03 implementation. System prompts already include behavioral constraints. Can revisit in a security hardening pass.
- **Blob storage for logs (#14)** — Premature optimization. DB storage with retention cleanup (already have RetentionCleanupService) is sufficient for a personal tool.
- **Split EstimatedCost/ActualCost (#20)** — Over-engineering. Single `Cost` field updated after execution completes is sufficient.
- **Typed parameters per capability (#22 recommendation)** — Dictionary<string,string> is simpler and sufficient for now. Type safety at the API boundary via FluentValidation.
- **Tool registry (#7)** — WriterAgent tools are internal (outline, expand). Formal registry is over-engineering for Phase 03. Can add if tool count grows.
