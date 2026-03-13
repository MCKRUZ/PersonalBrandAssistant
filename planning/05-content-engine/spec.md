# 05 — Content Engine

## Overview
The content creation, repurposing, calendar, and brand voice management system. Uses the agent orchestration layer (03) for AI-powered generation and the platform integration layer (04) for format-aware content creation.

## Requirements Reference
See `../requirements.md` for full project context and `../deep_project_interview.md` for design decisions.

Key interview insights:
- Three content flow patterns: linear pipeline, calendar-driven, reactive/proactive
- Blog is custom HTML deployed via git to matthewkruczek.ai
- Existing `matt-kruczek-blog-writer` Claude Code skill handles blog writing

## Scope

### Content Creation Pipeline
- **Topic → Outline → Draft → Final** lifecycle
- Topic sources: manual input, trend suggestions, content calendar, repurposed ideas
- AI-assisted outline generation (orchestrator creates outline, user refines)
- AI draft generation via WriterAgent (streaming to dashboard for real-time preview)
- Human editing and refinement in dashboard
- Final review before submission to workflow engine

### Blog Writing Integration
- Generate blog post HTML from topics/outlines using WriterAgent
- Output format: HTML compatible with matthewkruczek.ai's existing structure
- Git-based deployment: generate HTML → commit to blog repo → deploy
- Integration with existing `matt-kruczek-blog-writer` skill patterns
- SEO optimization: meta tags, heading structure, keyword density suggestions
- Image placeholder suggestions (for manual addition or future AI generation)

### Content Repurposing Engine
- **Blog → Social:** Extract key points from blog post → generate platform-specific social posts/threads
- **Social → Blog:** Aggregate high-engagement social posts → suggest blog topics
- **Cross-platform:** Adapt content from one platform's format to another
- Repurposing maintains content relationships (parent/child in domain model)
- AI-powered summarization and expansion as needed

### Content Calendar & Strategy
- Calendar management: create, edit, delete scheduled content slots
- Theme/topic assignment by time period (weekly themes, monthly focus areas)
- AI-powered calendar suggestions based on brand profile and past performance
- Calendar feeds scheduled slots into the workflow engine's queue
- Recurring content patterns (e.g., "Tip Tuesday", "Weekly roundup")

### Brand Voice System
- Brand profile definition: tone descriptors, style guidelines, vocabulary preferences, topics, persona
- Brand voice validation: check generated content against brand profile
- Voice examples: curated examples of on-brand content for few-shot prompting
- Brand consistency scoring (how well does generated content match the profile?)
- Brand profile is injected into all agent prompts via the prompt builder (03)

### Trend Monitoring & Proactive Suggestions
- Monitor trending topics in the user's domain/niche
- Suggest content ideas based on trends + brand profile alignment
- Prioritize suggestions by: trend momentum, brand relevance, content gap
- Sources: platform trending topics, RSS feeds, keyword monitoring

### Content Analytics Aggregation
- Pull engagement data from platforms (04) for each content piece
- Calculate cross-platform performance metrics
- Identify top-performing content types, topics, and posting times
- Feed insights back into content calendar suggestions
- Cost-per-engagement metrics (LLM cost vs engagement received)

## Out of Scope
- Agent framework and Claude API integration (→ 03)
- Platform API calls for posting and engagement (→ 04)
- Dashboard UI for content creation (→ 06)
- Workflow approval logic (→ 02)

## Key Decisions Needed During /deep-plan
1. How to integrate with existing blog-writer skill — call it programmatically or replicate its patterns?
2. Trend monitoring data sources — which APIs/feeds to use?
3. Content analytics aggregation frequency — real-time vs periodic batch?
4. Brand voice validation — rule-based, AI-based, or hybrid?
5. Content relationship model — flat references or tree structure?

## Dependencies
- **Depends on:** `01-foundation` (domain models), `03-agent-orchestration` (AI generation), `04-platform-integrations` (format constraints)
- **Blocks:** `06-angular-dashboard` (provides content APIs)

## Interfaces Consumed
- Domain models from 01 (Content, BrandProfile, ContentCalendar)
- `IAgentOrchestrator` from 03 (submit content generation tasks)
- `IPromptBuilder` from 03 (assemble prompts with brand voice)
- `IPlatformContentFormatter` from 04 (platform-specific formatting)
- `ISocialPlatform.GetEngagement()` from 04 (analytics data)

## Interfaces Produced
- `IContentPipeline` — create, advance, and manage content through creation stages
- `IRepurposingService` — transform content across formats/platforms
- `IContentCalendarService` — manage calendar, themes, scheduled slots
- `IBrandVoiceService` — manage profile, validate content, score consistency
- `ITrendMonitor` — fetch and prioritize trend suggestions
- Content and calendar API endpoints for dashboard

## Definition of Done
- Content creation pipeline works end-to-end (topic → draft → formatted output)
- Blog post generation produces valid HTML deployable to matthewkruczek.ai
- Content repurposing transforms blog → social posts for at least 2 platforms
- Content calendar CRUD operations work
- Brand voice profile configurable and injected into prompts
- Content analytics aggregated from at least 2 platforms
- Integration tests for pipeline and repurposing flows
