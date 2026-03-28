# Content Engine — Interview Transcript

## Q1: Trend-based content flow autonomy
**Q:** The spec mentions three content flow patterns: linear pipeline, calendar-driven, and reactive/proactive. For the reactive/proactive flow, how autonomous should this be?
**A:** Configurable via autonomy dial — tied to the existing autonomy level setting. Autonomous auto-generates, manual just suggests.

## Q2: Blog writing output format
**Q:** Should the content engine generate HTML directly, produce markdown, or integrate with the existing matt-kruczek-blog-writer skill?
**A:** Integrate with existing blog-writer skill.

## Q3: Content repurposing automation
**Q:** Should the system auto-generate all platform variants when a blog is published, or suggest opportunities?
**A:** Autonomy-driven. Autonomous: auto-generate all. SemiAuto: auto-generate from published parents. Manual: suggest only.

## Q4: Brand voice validation gate
**Q:** Should brand voice scoring block low-scoring content or be advisory only?
**A:** Configurable per autonomy level. Autonomous: hard gate (auto-regenerate). SemiAuto/Manual: advisory only.

## Q5: Content calendar auto-fill
**Q:** When a recurring calendar slot arrives, should the system auto-assign content from a backlog/queue?
**A:** Autonomy-driven. Autonomous: auto-fill. SemiAuto: suggest best fit. Manual: empty slots await assignment.

## Q6: Trend monitoring infrastructure
**Q:** What's your appetite for running Docker containers for trend monitoring on the Synology NAS?
**A:** Full self-hosted stack — run TrendRadar + FreshRSS + Reddit API polling as Docker containers.

## Q7: Content analytics aggregation
**Q:** Should engagement analytics be real-time, batch, or hybrid?
**A:** Hybrid — batch background job for periodic snapshots, plus on-demand refresh for real-time view.

## Q8: Content relationship model
**Q:** Should repurposed content use flat parent-child or tree structure?
**A:** Tree structure (multi-level). Support blog → tweet thread → individual highlights, and cross-platform social → social.

## Q9: Blog-writer skill integration path
**Q:** How should the content engine integrate with the matt-kruczek-blog-writer skill?
**A:** The entire application should use the claude-code-sidecar project at `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\claude-code-sidecar` — no direct Claude API calling.

## Q10: Sidecar architecture (research follow-up)
**Q:** The sidecar wraps Claude Code CLI via WebSocket. How does this integrate?
**Research Finding:** The sidecar is a TypeScript monorepo (pnpm workspaces) with:
- `packages/core/` — SubprocessManager spawns `claude -p "task" --output-format stream-json`
- `packages/web/` — Next.js 15 + WebSocket server at localhost:3001
- Client sends `{ type: "send-message", payload: { message } }` via WebSocket
- Server streams back `chat-event`, `file-change`, `status` events
- Session persistence via `--resume <sessionId>`
- Auth delegated to Claude Code's own credentials

## Q11: SDK vs Sidecar scope
**Q:** Should the content engine replace existing Anthropic SDK integration with sidecar for all agents, or use sidecar for content generation only?
**A:** Sidecar for everything — replace IChatClientFactory/Anthropic SDK entirely.

## Q12: Sidecar operation mode
**Q:** Should the sidecar edit files directly or just return generated text?
**A:** Full agent mode — let it edit files. Blog writing: edit files + git. Social posts handled by the sidecar too.

## Q13: Phase 03 migration scope
**Q:** Should phase 05 include refactoring phase 03's agent orchestration to use the sidecar?
**A:** Full rewrite of phase 03. Replace the entire agent orchestration layer with sidecar-based architecture.

## Q14: Docker Compose
**Q:** Should we include Docker Compose configuration for the sidecar?
**A:** Yes, include Docker setup — add sidecar service to docker-compose.yml with proper networking and config.

---

## Key Decisions Summary

1. **Autonomy dial is the universal control** — every feature (trends, repurposing, calendar fill, voice gating) respects the autonomy level
2. **Claude Code Sidecar replaces Anthropic SDK** — all AI goes through WebSocket to sidecar, which wraps Claude Code CLI
3. **Full agent mode** — sidecar edits files, commits to git, writes blog HTML directly
4. **Phase 03 full rewrite** — existing IChatClientFactory/AgentOrchestrator replaced with sidecar-based architecture
5. **Tree-structured content relationships** — multi-level repurposing (blog → thread → highlights, cross-platform)
6. **Full self-hosted trend stack** — TrendRadar + FreshRSS + Reddit on Docker
7. **Hybrid analytics** — background batch + on-demand refresh
8. **Docker Compose** — includes sidecar, trend monitoring containers
