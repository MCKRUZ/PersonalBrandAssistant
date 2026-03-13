---
paths: "**/Agents/**,**/Orchestration/**,**/LLM/**"
---
# AI Agent Rules

- Every agent capability must implement a common interface (e.g., `IAgentCapability`)
- LLM calls must include: retry logic, token tracking, structured output parsing
- Use dependency injection for LLM client — never instantiate directly
- Log all LLM interactions (prompt + response) for observability
- Include cost estimation before executing multi-step agent plans
- Agent outputs must be validated before posting to external platforms
