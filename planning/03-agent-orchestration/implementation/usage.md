# Phase 03: Agent Orchestration — Usage Guide

## Quick Start

### 1. Configure API Key

```bash
# Development (User Secrets)
dotnet user-secrets set "AgentOrchestration:ApiKey" "sk-ant-your-key-here" \
  --project src/PersonalBrandAssistant.Api
```

### 2. Run the API

```bash
dotnet run --project src/PersonalBrandAssistant.Api
```

### 3. Execute an Agent

```bash
# Non-streaming execution (fire-and-forget)
curl -X POST http://localhost:5000/api/agents/execute \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"type": "Writer"}'

# Non-streaming with wait for result
curl -X POST "http://localhost:5000/api/agents/execute?wait=true" \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"type": "Writer", "parameters": {"topic": "AI trends"}}'

# SSE streaming
curl -N -X POST http://localhost:5000/api/agents/stream \
  -H "X-Api-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"type": "Social", "contentId": "guid-here"}'
```

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/agents/stream` | SSE streaming execution |
| POST | `/api/agents/execute` | Non-streaming execution (`?wait=true` for sync) |
| GET | `/api/agents/executions/{id}` | Get execution status |
| GET | `/api/agents/executions` | List executions (`?contentId=` filter) |
| GET | `/api/agents/usage` | Token usage for date range (`?from=&to=`) |
| GET | `/api/agents/budget` | Current budget status |

## Agent Capabilities

| Type | Purpose | Creates Content |
|------|---------|----------------|
| `Writer` | Blog posts, articles, long-form content | Yes |
| `Social` | Social media posts (Twitter, LinkedIn, etc.) | Yes |
| `Repurpose` | Transform existing content for new platforms | Yes |
| `Engagement` | Reply suggestions, comment analysis | No |
| `Analytics` | Performance analysis, content insights | No |

## SSE Event Types

```
data: {"type":"status","status":"running"}
data: {"type":"token","text":"Generated text chunk"}
data: {"type":"usage","inputTokens":150,"outputTokens":42}
data: {"type":"complete","executionId":"guid","createdContentId":"guid"}
data: {"type":"error","message":"Budget exceeded"}
```

## Configuration (appsettings.json)

```json
{
  "AgentOrchestration": {
    "DailyBudget": 10.00,
    "MonthlyBudget": 100.00,
    "DefaultModelTier": "Standard",
    "Models": {
      "Fast": "claude-haiku-4-5",
      "Standard": "claude-sonnet-4-5-20250929",
      "Advanced": "claude-opus-4-6"
    },
    "Pricing": {
      "claude-haiku-4-5": { "InputPerMillion": 1.00, "OutputPerMillion": 5.00 },
      "claude-sonnet-4-5-20250929": { "InputPerMillion": 3.00, "OutputPerMillion": 15.00 },
      "claude-opus-4-6": { "InputPerMillion": 5.00, "OutputPerMillion": 25.00 }
    },
    "MaxRetriesPerExecution": 3,
    "ExecutionTimeoutSeconds": 180
  }
}
```

## Architecture

```
API Layer (Endpoints)
  └── IAgentOrchestrator
        ├── Routes tasks to IAgentCapability by type
        ├── Budget check via ITokenTracker
        ├── Retry with exponential backoff + model tier downgrade
        ├── Creates Content entities for content-producing capabilities
        └── Records usage via ITokenTracker
              ├── IAgentCapability (5 implementations)
              │     └── Uses IChatClient via AgentContext
              ├── IPromptTemplateService (Fluid/Liquid templates)
              ├── IChatClientFactory (Anthropic SDK wrapper)
              └── ITokenTracker (cost tracking + budget enforcement)
```

## Testing

```bash
# Run all tests (443 tests)
dotnet test

# Run agent-specific tests
dotnet test --filter "AgentOrchestrator|AgentCapability|AgentEndpoints|AgentServiceRegistration"
```

### Test Mocks

- `MockChatClient` — configurable response text, token counts, failure simulation
- `MockChatClientFactory` — returns MockChatClient instances
- `AsyncQueryableHelpers` — EF Core async DbSet mocking
