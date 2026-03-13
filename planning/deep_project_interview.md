# Deep Project Interview — Personal Brand Assistant

**Date:** 2026-03-13

## Interview Transcript

### Q1: Dashboard Role
**Question:** How do you see the relationship between the dashboard (Angular frontend) and the AI agent logic?
**Answer:** Full workspace — the dashboard is where you actively create content (write posts, draft blogs with AI assistance), plus monitoring and approval.

### Q2: MVP Priority
**Question:** What's the MVP you'd want working first?
**Answer:** Full pipeline — Blog → social media → analytics as one connected flow from day one.

### Q3: Agent Autonomy
**Question:** How autonomous should the agent be?
**Answer:** Configurable autonomy dial — from fully human-in-the-loop to fully autonomous. The user wants to "turn the dial both ways." This means the approval/workflow engine is a core architectural component, not an afterthought.

### Q4: Social Platforms
**Question:** Which platforms at launch?
**Answer:** All four — Twitter/X, LinkedIn, Instagram, and YouTube.

### Q5: Blog Platform
**Question:** How does matthewkruczek.ai work?
**Answer:** Custom HTML deployed via git. The existing `matt-kruczek-blog-writer` skill already handles blog writing and deployment. The system needs to integrate with this git-based publishing workflow.

### Q6: Content Flow
**Question:** How should content flow through the system?
**Answer:** All three patterns:
1. **Linear pipeline:** Topic → Draft → Approve → Publish → Repurpose
2. **Content calendar driven:** Set strategy/themes, agent fills in content to match schedule
3. **Reactive + proactive:** Agent monitors trends and suggests content, user can also feed topics on demand

### Q7: Agent Architecture
**Question:** Single agent or multi-agent?
**Answer:** Hybrid — single orchestrator for simple tasks, spawns specialized sub-agents for complex work (e.g., long-form blog writing).

### Q8: Deployment
**Question:** Deployment preference?
**Answer:** Self-hosted on Synology NAS via Docker containers.

### Q9: Database
**Question:** Database choice?
**Answer:** PostgreSQL — lightweight, great Docker support, good JSON features for flexible content storage.

## Key Decisions Summary

| Decision | Choice | Impact |
|----------|--------|--------|
| Frontend role | Full workspace | Significant Angular complexity, real-time features needed |
| MVP scope | Full pipeline | Must build core of everything, can't skip any layer |
| Autonomy | Configurable dial | Workflow engine is foundational, not optional |
| Platforms | All 4 (X, LinkedIn, Instagram, YouTube) | 4 OAuth integrations, 4 API adapters |
| Blog | Git-deployed HTML | Integration with existing blog-writer skill |
| Content flow | All 3 patterns | Flexible content engine, calendar + reactive + pipeline |
| Agent design | Hybrid (orchestrator + sub-agents) | Need clean agent abstraction layer |
| Deployment | Synology Docker | Docker Compose, no cloud dependencies |
| Database | PostgreSQL | EF Core + Npgsql, Docker container |

## Architectural Observations

1. **The workflow/approval engine is the heart of the system.** Every content piece flows through it. The autonomy dial controls how much human intervention is required at each stage.

2. **Four platform integrations are a significant surface area.** Each has different OAuth flows, API patterns, content formats, and rate limits. An abstraction layer (`ISocialPlatform`) is essential.

3. **The hybrid agent architecture suggests a clean separation between:** the orchestrator (routing, scheduling, workflow management) and capability agents (writing, social posting, analytics).

4. **Content is the central domain object.** A piece of content can be: a blog draft, a social post, a thread, a video description — with relationships between them (blog → thread → individual posts).

5. **The content calendar is essentially a scheduling + strategy layer** on top of the content pipeline. It's not a separate system but an input mechanism that feeds the same workflow engine.
