<!-- SPLIT_MANIFEST
01-foundation
02-workflow-engine
03-agent-orchestration
04-platform-integrations
05-content-engine
06-angular-dashboard
END_MANIFEST -->

# Project Manifest — Personal Brand Assistant

## Overview

AI-powered personal brand assistant with configurable autonomy, multi-platform social media management, blog writing, content repurposing, and analytics. Built on .NET 10 + Angular 19 + PostgreSQL, self-hosted via Docker on Synology NAS.

## Split Structure

### 01-foundation
**Purpose:** Solution scaffolding, shared infrastructure, and domain model.

**Scope:**
- .NET 10 solution structure (Clean Architecture / Vertical Slices)
- PostgreSQL + EF Core setup with migrations
- Docker Compose for local dev (API + DB + Angular)
- Shared domain models (Content, Platform, User, BrandProfile)
- Authentication & API structure
- Logging, error handling, health checks
- CI basics (build + test)

**Produces:** Solution structure, domain models, database schema, Docker environment, API skeleton

**Dependencies:** None — this is the foundation everything else builds on.

---

### 02-workflow-engine
**Purpose:** The configurable autonomy dial — content state machine, approval workflows, and scheduling.

**Scope:**
- Content lifecycle state machine (Draft → Review → Approved → Scheduled → Published → Archived)
- Configurable approval rules (per content type, per platform, per autonomy level)
- The "autonomy dial" — settings that control how much human intervention is needed
- Queue-based async processing (background jobs for scheduled publishing)
- Notification system (content ready for review, published, failed)
- Audit trail for all content state transitions

**Produces:** Workflow API, background job infrastructure, approval endpoints

**Dependencies:**
- `01-foundation` — domain models, database, API structure

---

### 03-agent-orchestration
**Purpose:** Hybrid AI agent layer — orchestrator + specialized sub-agents.

**Scope:**
- Claude API integration via Anthropic .NET SDK
- Agent orchestrator (routes tasks to appropriate handler)
- Sub-agent framework (spawn specialized agents for complex tasks)
- Agent capability interface (`IAgentCapability`)
- Token usage tracking and cost management
- Prompt management (templates, brand voice injection)
- Structured output parsing and validation
- Retry logic and error handling for LLM calls

**Produces:** Agent orchestration API, capability interfaces, prompt infrastructure

**Dependencies:**
- `01-foundation` — domain models, database
- `02-workflow-engine` — agent outputs feed into workflow (drafts need approval)

---

### 04-platform-integrations
**Purpose:** OAuth + API adapters for Twitter/X, LinkedIn, Instagram, YouTube.

**Scope:**
- `ISocialPlatform` abstraction layer
- Twitter/X API v2 integration (OAuth 2.0, tweet/thread posting, media upload)
- LinkedIn API integration (OAuth 2.0, post/article publishing)
- Instagram Graph API integration (via Facebook/Meta, media posting)
- YouTube Data API integration (OAuth 2.0, video metadata, community posts)
- OAuth token encrypted storage and refresh flows
- Platform-specific content formatting (character limits, hashtags, media requirements)
- Rate limiting per platform (respect API quotas)
- Webhook receivers for engagement notifications (where supported)

**Produces:** Platform adapter implementations, OAuth management, content formatting utilities

**Dependencies:**
- `01-foundation` — domain models, encrypted storage infrastructure
- `02-workflow-engine` — publishing triggers from workflow engine

---

### 05-content-engine
**Purpose:** Content creation, repurposing, calendar, and brand voice management.

**Scope:**
- Content creation pipeline (Topic → Outline → Draft → Final)
- Blog writing integration (HTML generation, git deployment to matthewkruczek.ai)
- Content repurposing engine (blog → social threads, social → blog topics)
- Content calendar & strategy management
- Brand voice profile (tone, style, keywords, topics, persona)
- Content templates and formatting rules
- SEO optimization suggestions
- Trend monitoring and proactive content suggestions
- Content analytics aggregation

**Produces:** Content APIs, repurposing pipelines, calendar management, brand voice system

**Dependencies:**
- `01-foundation` — domain models, database
- `03-agent-orchestration` — AI-powered content generation and repurposing
- `04-platform-integrations` — platform-specific formatting constraints

---

### 06-angular-dashboard
**Purpose:** Full workspace UI — content creation, approval, monitoring, analytics, settings.

**Scope:**
- Angular 19 with standalone components, NgRx signals for state management
- Content workspace (editor, AI assist panel, preview)
- Approval queue (review pending content, approve/reject/edit)
- Content calendar view (drag-and-drop scheduling)
- Analytics dashboard (post performance, audience growth, platform comparison)
- Platform connection management (OAuth connect/disconnect, status)
- Brand voice settings (profile editor, tone examples)
- Autonomy dial settings UI
- Real-time updates (SignalR for live content status, notifications)
- Responsive design for desktop-first (with tablet support)

**Produces:** Complete Angular application

**Dependencies:**
- `01-foundation` — API contracts
- `02-workflow-engine` — approval/workflow APIs
- `05-content-engine` — content and calendar APIs

---

## Dependency Graph

```
01-foundation
├── 02-workflow-engine
│   ├── 03-agent-orchestration
│   │   └── 05-content-engine
│   ├── 04-platform-integrations
│   └── 06-angular-dashboard
└── (all splits depend on 01)
```

## Execution Order

**Wave 1:** `01-foundation` (must complete first)

**Wave 2 (parallel):** `02-workflow-engine` (only needs 01)

**Wave 3 (parallel):** `03-agent-orchestration` + `04-platform-integrations` (both need 01 + 02)

**Wave 4:** `05-content-engine` (needs 01 + 03 + 04)

**Wave 5:** `06-angular-dashboard` (needs 01 + 02 + 05, but can start skeleton in Wave 2)

> **Note:** The Angular dashboard can begin scaffolding and UI component work in Wave 2 using mock data, with real API integration happening in Wave 5.

## Cross-Cutting Concerns

- **Brand voice** is injected by the content engine into all agent prompts
- **The workflow engine** is the central nervous system — every content piece flows through it
- **Docker Compose** must support the full stack for local dev from Wave 1 onward
- **Platform rate limits** need centralized management (likely in 04, consumed by 02's scheduler)
- **Cost tracking** for LLM usage spans 03 and 05

## /deep-plan Commands

Execute in order:
```
/deep-plan @planning/01-foundation/spec.md
/deep-plan @planning/02-workflow-engine/spec.md
/deep-plan @planning/03-agent-orchestration/spec.md
/deep-plan @planning/04-platform-integrations/spec.md
/deep-plan @planning/05-content-engine/spec.md
/deep-plan @planning/06-angular-dashboard/spec.md
```
