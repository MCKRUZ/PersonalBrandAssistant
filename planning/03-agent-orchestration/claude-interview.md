# Phase 03 Agent Orchestration — Interview Transcript

## Q1: Claude API SDK Choice
**Question:** For the Claude API integration, should we use the official Anthropic NuGet package (v12.8.0) with IChatClient, or do you have a preference?

**Answer:** Official Anthropic SDK (v12.8.0) with IChatClient bridge.

## Q2: Agent State Persistence
**Question:** How should agent state be persisted? In-memory is simpler but lost on restart; DB enables monitoring and recovery.

**Answer:** Database. AgentExecution entity tracks runs, enables dashboard monitoring and crash recovery.

## Q3: First Sub-Agent to Implement
**Question:** Which sub-agent should we implement end-to-end first as the proof-of-concept?

**Answer:** All of them — SocialAgent, WriterAgent, and RepurposeAgent should all be implemented in Phase 03. (Note: EngagementAgent and AnalyticsAgent are also in scope per the spec.)

## Q4: Prompt Template Storage
**Question:** Should we store prompt templates as embedded resource files in the repo, or in a separate prompts/ directory?

**Answer:** prompts/ directory — Liquid templates in prompts/ folder, Git-versioned, easy to edit.
