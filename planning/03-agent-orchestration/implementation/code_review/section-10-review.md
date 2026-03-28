# Code Review: Section 10 -- API Endpoints

## Critical
1. SSE error event leaks internal error messages - should sanitize like ToHttpResult
2. Static mocks in tests are thread-unsafe

## Important
3. No input validation on AgentExecuteRequest
4. SSE returns 200 before validation
5. ListExecutions has no pagination
6. Hardcoded test credentials, DRY violation

## Minor
7. WriteSseEventAsync allocates per event
8. Unused BodyWriter variable
9. AgentExecuteRequest defined inline instead of own file
10. Missing test for invalid request body

## Suggestions
11. Add OpenAPI metadata
12. SSE: add event ID and retry fields
13. Document default date range for usage endpoint
