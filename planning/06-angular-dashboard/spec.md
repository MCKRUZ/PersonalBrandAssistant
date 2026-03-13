# 06 — Angular Dashboard

## Overview
Full workspace UI built with Angular 19 — content creation, approval queue, content calendar, analytics, platform management, and settings. This is the primary interface for interacting with the Personal Brand Assistant.

## Requirements Reference
See `../requirements.md` for full project context and `../deep_project_interview.md` for design decisions.

Key interview insight: Dashboard is a "full workspace" — not just monitoring, but where content is actively created with AI assistance.

## Scope

### Core Architecture
- Angular 19 with standalone components (no NgModules)
- NgRx signals for state management
- Lazy-loaded feature modules for each major area
- SignalR integration for real-time updates (content status, agent activity, notifications)
- Responsive design: desktop-first with tablet support
- Component library / design system (PrimeNG, Angular Material, or custom — decide during /deep-plan)

### Content Workspace
- Rich text editor for content creation (blog posts, social posts)
- AI assist panel: request AI suggestions, see streaming generation in real-time
- Content preview: see how content will look on each target platform
- Multi-platform targeting: select platforms and see format requirements
- Draft management: save, resume, discard drafts
- Template selection for common content types

### Approval Queue
- List of content awaiting review (filterable by type, platform, urgency)
- Inline preview with approve/reject/edit actions
- Batch approval for routine content
- Diff view for AI-edited content (show what the agent changed)
- Approval history and audit trail

### Content Calendar
- Monthly/weekly/daily calendar views
- Drag-and-drop scheduling
- Color-coded by platform and content type
- Theme/focus area overlays
- Slot creation: create empty slots for the agent to fill
- Calendar-to-queue integration (see what's scheduled and what's published)

### Analytics Dashboard
- Post performance charts (engagement, reach, clicks per post)
- Platform comparison views
- Audience growth over time
- Best time to post heatmap
- Content type performance breakdown
- LLM cost tracking and budget status
- Date range filtering, export to CSV

### Platform Management
- OAuth connection flow for each platform (connect/disconnect)
- Connection status indicators (healthy, token expiring, disconnected)
- Platform-specific settings (posting preferences, hashtag defaults)
- Rate limit status visibility

### Settings & Configuration
- Autonomy dial UI (global + per-type + per-platform controls)
- Brand voice profile editor (tone, style, examples)
- Notification preferences
- LLM budget and model preferences
- Content calendar strategy/theme configuration

### Notifications
- In-app notification center
- Real-time toast notifications for important events (published, failed, needs review)
- Notification preferences (what to show, what to suppress)

## Out of Scope
- Backend API implementation (→ 01-05)
- Mobile app (future consideration)

## Key Decisions Needed During /deep-plan
1. Component library: PrimeNG vs Angular Material vs Tailwind + custom components?
2. Rich text editor: TipTap, Quill, ProseMirror, or ngx-editor?
3. Chart library: ngx-charts, Chart.js, or ECharts?
4. Calendar component: FullCalendar Angular wrapper or custom?
5. Real-time: SignalR vs Server-Sent Events for live updates?

## Dependencies
- **Depends on:** `01-foundation` (API contracts), `02-workflow-engine` (approval APIs), `05-content-engine` (content and calendar APIs)
- **Blocks:** Nothing (final layer)

## Interfaces Consumed
- All API endpoints from 01-05
- SignalR hubs from 02 (workflow notifications) and 03 (agent activity)
- Content APIs from 05 (pipeline, calendar, brand voice)
- Platform APIs from 04 (connection management, status)

## Parallel Work Strategy
- Can begin scaffolding, design system, and component development in Wave 2 using mock data
- Real API integration happens in Wave 5 as backend splits complete
- Use Angular environments + interceptors for mock/real API switching

## Definition of Done
- All major views implemented and functional with real API integration
- Content workspace with AI assist panel working end-to-end
- Approval queue processes content through review workflow
- Content calendar with drag-and-drop scheduling
- Analytics dashboard with at least 3 chart types
- Platform OAuth connection flow working for all 4 platforms
- Settings pages for autonomy dial and brand voice
- Real-time notifications via SignalR
- Responsive on desktop and tablet viewports
- Lighthouse score > 80 for performance
