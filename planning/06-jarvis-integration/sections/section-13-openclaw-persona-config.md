# Section 13: OpenClaw Persona Configuration

## Overview

Configures the OpenClaw Gateway to register PBA's MCP server and updates the Jarvis persona files to document PBA capabilities. This section is a configuration-only change -- no code is written, only JSON and markdown files are updated. It connects the MCP server (sections 04-09) to the Jarvis agent ecosystem so Jarvis can invoke PBA tools through voice commands and chat.

Three changes:
1. **MCP Server Registration**: Add PBA to OpenClaw's `openclaw.json` so the gateway can spawn the MCP process.
2. **Jarvis Persona tools.md**: Document all 13 PBA MCP tools so the LLM knows when and how to use them.
3. **Jarvis Persona heartbeat.md**: Update the morning briefing schedule to include PBA data.

## Dependencies

- **section-05-mcp-content-pipeline-tools**: 4 tools must be implemented.
- **section-06-mcp-calendar-tools**: 3 tools must be implemented.
- **section-07-mcp-analytics-tools**: 3 tools must be implemented.
- **section-08-mcp-social-tools**: 3 tools must be implemented.
- **section-09-mcp-idempotency-audit**: Idempotency and audit trail must be in place so tools have full production behavior.

## Tests

This section is configuration and documentation only. No automated tests are needed. Validation is manual:

- Verify OpenClaw starts successfully with the PBA MCP server configured.
- Verify the PBA MCP process responds to an MCP `initialize` request via stdio.
- Verify Jarvis can invoke a PBA tool via voice command (e.g., "what's trending") and receive a response.
- Verify the morning briefing includes PBA data.

## File Paths

### Modified Files

- `openclaw.json` (or the OpenClaw workspace configuration file) -- Add PBA MCP server entry.
- `jarvis-persona/jarvis/tools.md` -- Add PBA tool documentation section.
- `jarvis-persona/jarvis/heartbeat.md` -- Add PBA briefing steps.

## MCP Server Registration

Add PBA to the `mcpServers` section of OpenClaw's configuration file. The configuration tells OpenClaw how to spawn the PBA MCP server process:

```json
{
  "mcpServers": {
    "pba": {
      "command": "dotnet",
      "args": ["/volume1/docker/pba/publish/PersonalBrandAssistant.Api.dll", "--mcp"],
      "env": {
        "ConnectionStrings__DefaultConnection": "${PBA_DB_CONNECTION}",
        "ApiKey": "${PBA_API_KEY}"
      }
    }
  }
}
```

Key details:
- **command**: `dotnet` -- Runs the published .NET assembly.
- **args**: The path to the published PBA API DLL followed by `--mcp`. The `--mcp` flag triggers MCP mode in `Program.cs`, starting the stdio transport instead of the HTTP server (see section 04).
- **env**: Environment variables passed to the MCP process:
  - `ConnectionStrings__DefaultConnection`: PostgreSQL connection string. The MCP process connects directly to the database (it does not go through HTTP).
  - `ApiKey`: Not used for authentication in MCP mode, but may be needed by internal services.
- The `${...}` tokens are resolved from OpenClaw's environment at startup.

The published binary path (`/volume1/docker/pba/publish/`) is the output of `dotnet publish` for the PBA API project. This binary must be built and placed at this path before OpenClaw can spawn it. The Docker Compose setup should mount this path or include it in the PBA stack's published artifacts.

## Jarvis Persona -- tools.md Update

Add a new section to `jarvis-persona/jarvis/tools.md` documenting all 13 PBA MCP tools. This file is read by the LLM as part of Jarvis's system prompt, so the descriptions directly influence tool selection behavior.

### Content to Add

```markdown
## Personal Brand Assistant (PBA)

PBA manages content creation, scheduling, social engagement, and analytics across Twitter, LinkedIn, Reddit, and Blog platforms. Use PBA tools when the user asks about their content, posts, social media, or personal brand.

**Important:** PBA has an autonomy dial. Write operations (create, publish, schedule, respond) may execute immediately or queue for approval depending on the current autonomy setting. If an action is queued, inform the user.

### Content Pipeline

| Tool | When to Use | Notes |
|------|------------|-------|
| `pba_create_content(topic, platform, contentType)` | "Write a post about X", "Create LinkedIn content about Y", "Draft something about Z" | Returns content ID and status. May queue for approval. Accepts optional `clientRequestId` for idempotency. |
| `pba_get_pipeline_status(contentId?)` | "What's in the pipeline?", "Status of my post", "Show content progress" | Omit contentId to list all active items. Returns stage, platform, last updated. |
| `pba_publish_content(contentId)` | "Publish my post", "Push the LinkedIn article live", "Send it out" | Content must be in Approved state. Accepts optional `clientRequestId`. |
| `pba_list_drafts(status?, platform?)` | "Show my drafts", "What posts are pending?", "List LinkedIn content" | Filters by status and/or platform. Returns all non-terminal items when no filters. |

### Content Calendar

| Tool | When to Use | Notes |
|------|------------|-------|
| `pba_get_calendar(startDate, endDate, platform?)` | "What's scheduled this week?", "Show my content calendar", "Any posts going out tomorrow?" | Dates in ISO 8601 format. Optional platform filter. |
| `pba_schedule_content(contentId, dateTime, platform)` | "Schedule this for Thursday at 9am", "Post it on LinkedIn tomorrow morning" | Validates against calendar conflicts. Content must exist. Accepts optional `clientRequestId`. |
| `pba_reschedule_content(contentId, newDateTime)` | "Move that post to Friday", "Reschedule the LinkedIn article to next week" | Validates new slot availability. Accepts optional `clientRequestId`. |

### Analytics & Trends

| Tool | When to Use | Notes |
|------|------------|-------|
| `pba_get_trends(limit?)` | "What's trending?", "Any hot topics?", "What should I write about?" | Returns topics sorted by relevance with source attribution. Default limit: 10. |
| `pba_get_engagement_stats(startDate, endDate, platform?)` | "How are my posts doing?", "Show engagement stats", "LinkedIn performance this week" | Returns per-platform breakdown with totals and daily averages. |
| `pba_get_content_performance(contentId)` | "How did that post do?", "Performance of my LinkedIn article" | Returns platform-specific metrics. Content must be published. |

### Social Engagement

| Tool | When to Use | Notes |
|------|------------|-------|
| `pba_get_opportunities(platform?, limit?)` | "Any comments to reply to?", "Show engagement opportunities", "What should I respond to?" | Returns opportunities ranked by relevance. |
| `pba_respond_to_opportunity(opportunityId, responseText?)` | "Reply to that comment", "Respond to the mention", "Engage with this thread" | Omit responseText to auto-generate a response. May queue for approval. Accepts optional `clientRequestId`. |
| `pba_get_inbox(platform?, unreadOnly?)` | "Check my inbox", "Any new messages?", "Show unread mentions" | Returns mentions, DMs, comments. Set unreadOnly=true for new items only. |

### Common Patterns

- When the user asks "how's my brand doing?" or "give me a status update", combine `pba_get_pipeline_status()`, `pba_get_engagement_stats()` for the last 7 days, and `pba_get_trends()` to give a comprehensive overview.
- When the user says "write something about X", use `pba_create_content` then `pba_get_pipeline_status` to check on progress.
- When the user says "publish everything that's ready", use `pba_list_drafts(status: "Approved")` then `pba_publish_content` for each item.
- Always include the `clientRequestId` parameter on write operations to prevent accidental duplicates from voice command retries.
```

### Description Strategy

The tool descriptions in `tools.md` serve as the primary mechanism for LLM tool selection. Each row includes:
- **Tool name with parameters**: Shows the function signature so the LLM knows what arguments to pass.
- **When to Use**: Example trigger phrases that map to this tool. These directly influence when the LLM decides to invoke the tool.
- **Notes**: Constraints, default behavior, and the `clientRequestId` reminder for write tools.

The "Common Patterns" section teaches the LLM multi-tool workflows, improving the quality of responses to complex or ambiguous requests.

## Jarvis Persona -- heartbeat.md Update

Add PBA to the morning briefing schedule in `jarvis-persona/jarvis/heartbeat.md`. The heartbeat file defines what Jarvis does during its periodic check-in (morning briefing, evening summary).

### Content to Add

Add a new briefing step in the morning schedule:

```markdown
### Content & Brand Status

1. Call `pba_get_calendar(today, today+7d)` to get the week's scheduled content.
2. Call `pba_get_engagement_stats(today-7d, today)` to get rolling 7-day engagement.
3. Call `pba_get_trends(limit: 5)` to get top trending topics.
4. Call `pba_get_opportunities(limit: 5)` to check for high-priority engagement opportunities.

Summarize as:
- "Content: N posts scheduled this week. Next publish: [platform] at [time]."
- "Engagement: [7-day total]. [Notable highlights if any -- viral posts, engagement drops]."
- "Trends: [Top 1-2 trending topics relevant to the brand]."
- "Inbox: [N unread items / engagement opportunities requiring attention]."

If the content queue is empty or nothing is scheduled in the next 48 hours, flag this as a concern: "No content scheduled in the next 2 days -- consider creating new posts."
```

This step should be placed after infrastructure health checks but before the day's task planning, since content status informs what actions Jarvis might suggest for the day.

## Environment Variables

The following environment variables must be set in OpenClaw's environment (or its Docker Compose configuration) for the PBA MCP server to work:

- `PBA_DB_CONNECTION`: PostgreSQL connection string for PBA's database. Format: `Host=192.168.50.x;Port=5432;Database=pba;Username=pba;Password=...`
- `PBA_API_KEY`: The write-scoped API key for PBA. Although MCP mode does not use HTTP authentication, some internal services may reference this key.

These are separate from the readonly key used by jarvis-monitor and jarvis-hud. The MCP process gets the write key because it performs write operations (create content, publish, schedule, respond).

## Deployment Notes

1. **Build the published binary**: Run `dotnet publish -c Release -o /volume1/docker/pba/publish/ src/PersonalBrandAssistant.Api/` to produce the standalone binary at the path OpenClaw expects.
2. **Verify the binary works in MCP mode**: Run `echo '{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}},"id":1}' | dotnet /volume1/docker/pba/publish/PersonalBrandAssistant.Api.dll --mcp` and confirm a valid MCP initialize response.
3. **Update OpenClaw configuration**: Add the `pba` entry to `openclaw.json`.
4. **Restart OpenClaw Gateway**: The gateway needs to reload its configuration to pick up the new MCP server.
5. **Update persona files**: Copy updated `tools.md` and `heartbeat.md` to the Jarvis persona directory.
6. **Test end-to-end**: Issue a voice command through Jarvis like "what's trending in my brand" and verify the response uses PBA data.

## Implementation Notes

- The `tools.md` content is loaded into the LLM's system prompt context on every Jarvis interaction. Keep descriptions concise but complete -- the LLM needs enough information to select the right tool but not so much that it wastes context window space.
- The heartbeat PBA step makes 4 MCP tool calls. These execute sequentially within the briefing workflow. Total expected latency: ~5-10 seconds (each call hits the database and returns JSON).
- The published binary path should be consistent across deployments. If using Docker volumes, mount the publish output directory so OpenClaw can access it from its container.
- The `--mcp` flag is the only mechanism that distinguishes MCP mode from HTTP mode. No other configuration is needed -- the MCP process reads the same `appsettings.json` and environment variables as the HTTP API.
- OpenClaw spawns the MCP process on demand when a tool call is needed, and may keep it alive for subsequent calls within a conversation. The process startup time (~1-2 seconds for .NET) is acceptable since it only happens once per conversation session.
