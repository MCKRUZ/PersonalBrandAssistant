# Autonomous LinkedIn Content Workflow

## Overview

Build an end-to-end automated content pipeline that runs daily: picks the top trending topic from existing TrendMonitor, generates a LinkedIn post via ContentPipeline with brand voice, generates an image (DALL-E or HuggingFace FLUX), and auto-publishes via the existing PublishingPipeline + LinkedInPlatformAdapter.

## Inspiration

Based on a Medium article describing an n8n workflow that automates LinkedIn posting:
1. **Scheduled trigger** — fires daily at 9AM
2. **Trending topic search** — real-time web search (Tavily API)
3. **Topic extraction** — picks the top result, extracts title + snippet
4. **AI content writing** — agent researches topic and writes a publication-ready LinkedIn post
5. **Image prompt generation** — second AI agent reads the post and creates a visual prompt
6. **Image generation** — text-to-image model (HuggingFace FLUX.1-schnell)
7. **Auto-publish to LinkedIn** — posts text + image via LinkedIn API

## What Already Exists in PBA

The Personal Brand Assistant already has most building blocks:

| Capability | Service | Status |
|-----------|---------|--------|
| Trend monitoring | `TrendMonitor` + 8 pollers (TrendRadar, FreshRSS, Reddit, HN, RSS, YouTube, Browser, Email) | Production-ready |
| Topic extraction | `TrendMonitor.RefreshTrendsAsync()` with LLM clustering + relevance scoring | Production-ready |
| AI content writing | `ContentPipeline` with outline-to-draft via Claude sidecar + brand voice | Production-ready |
| LinkedIn publishing | `PublishingPipeline` + `LinkedInPlatformAdapter` with retry, idempotency, OAuth | Production-ready |
| Content workflow | `WorkflowEngine` state machine (Draft -> Review -> Approved -> Scheduled -> Publishing -> Published) | Production-ready |
| Scheduled publishing | `ScheduledPublishProcessor` — background service that checks for due content every 30s | Production-ready |
| Trend polling | `TrendAggregationProcessor` — interval-based background service | Partial (interval, not time-based) |

## What's Missing

1. **Daily time-based trigger** — TrendAggregationProcessor is interval-based, needs configurable time-of-day scheduling (e.g., "run at 9AM daily")
2. **End-to-end orchestrator** — No service chains the full flow: pick trend -> generate content -> generate image -> schedule/publish
3. **Image prompt generation** — No service to extract visual themes from a post and create an image generation prompt
4. **Image generation integration** — No DALL-E, HuggingFace FLUX, or Stable Diffusion API integration
5. **Autonomous content pipeline** — Current flow requires manual triggering through UI; needs full auto mode

## Key Requirements

### 1. Daily Time-Based Trigger
- Configurable time (default 9AM local time)
- Timezone-aware scheduling
- Configurable days (weekdays only, every day, custom)
- Should integrate with existing `TrendAggregationProcessor` or be a new `DailyContentAutomationProcessor`

### 2. Orchestrator Service
- Chains: trend selection -> content generation -> image generation -> scheduling/publishing
- Respects existing autonomy levels (SemiAuto vs Autonomous)
- In Autonomous mode: full auto, no review step
- In SemiAuto mode: generates content but pauses for human review before publishing
- Error handling with retry and notification on failure
- Tracks each run with status (success/partial/failed)

### 3. Image Prompt Generation
- AI service that reads the generated post content
- Extracts key visual themes and concepts
- Generates a prompt suitable for image generation models
- Should produce professional, LinkedIn-appropriate visuals (not stock photos)
- Style guidance: gradients, clean typography, flat-style infographics, editorial feel

### 4. Image Generation Service
- Integration with external API (DALL-E 3 or HuggingFace FLUX.1-schnell)
- Downloads generated image and stores via existing `IMediaStorage`
- Associates image with Content entity
- Fallback: publish without image if generation fails (don't block the post)

### 5. Configurable Autonomy
- Leverage existing WorkflowEngine autonomy levels
- Full Auto: trend -> write -> image -> publish (zero human input)
- Semi-Auto: trend -> write -> image -> queue for review -> human approves -> publish
- Manual: trend -> notify user of suggestion -> user decides everything

## Architecture Constraints

- Must integrate with existing services, not replace them
- All LLM calls go through the sidecar (existing pattern)
- Image generation is the only new external API integration
- Publishing must go through existing PublishingPipeline (already handles retry, idempotency, rate limiting)
- Content must go through WorkflowEngine state machine transitions
- New services should follow existing patterns (DI, interfaces, Result<T>)

## Non-Goals

- Not building a full n8n replacement or generic workflow engine
- Not supporting other platforms in this phase (LinkedIn only for orchestration; existing platform adapters remain)
- Not building a custom image generation model
- Not replacing existing trend sources (keep all 8 pollers)
