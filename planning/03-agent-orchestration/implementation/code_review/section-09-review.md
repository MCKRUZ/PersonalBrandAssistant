# Code Review: Section 09 -- AgentOrchestrator

## Critical

1. **Budget check race condition (TOCTOU)** - Budget checked once at top but concurrent executions can blow past limit
2. **ExecuteAsync method too long (~130 lines)** - Violates 50-line guideline

## Important

3. **No backoff delay between retries** - 429s will fire again instantly
4. **execution.ModelUsed not updated after tier downgrade** - Incorrect cost tracking
5. **CreateContentFromOutputAsync ignores workflow transition failure** - Content left in undefined state
6. **Capability failure vs thrown exception not tested separately** - Misleading test name
7. **ListExecutionsAsync has no pagination** - Can load thousands of records
8. **No validation on ExecutionTimeoutSeconds/MaxRetriesPerExecution** - Zero/negative values cause issues

## Minor

9. **_capabilities dictionary is mutable** - Should use FrozenDictionary
10. **Duplicate capability types crash with confusing error** - Need guard
11. **MapCapabilityToContentType has silent default** - Should throw for unexpected types
12. **Test file approaching size limit** - 438 lines

## Suggestions

13. Missing test coverage: timeout, ContentId loading, ListExecutionsAsync, no active brand profile fallback
14. Sanitize error messages in notifications
15. Consider making AgentOrchestrationOptions a record
