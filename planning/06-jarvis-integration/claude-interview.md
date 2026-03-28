# Interview Transcript: Jarvis ↔ PBA Integration

## Q1: MCP Server Location
**Q:** Should the MCP server be a separate project in the PBA solution (e.g., PersonalBrandAssistant.McpServer) or a new standalone repo?

**A:** Built into the API project. Add MCP endpoints directly to the existing API (dual-protocol server).

## Q2: HUD Layout
**Q:** For the jarvis-hud integration, should PBA get its own dedicated page/route, or should PBA data be woven into existing panels?

**A:** Both. PBA data in existing panels (MetricCards, AlertFeed, BriefingPanel) + a dedicated /content page for deep-dive.

## Q3: Docker Networking
**Q:** How should PBA and Jarvis stacks communicate in Docker?

**A:** Keep separate, use host IP. Jarvis monitor hits PBA via LAN IP (192.168.x.x). Full isolation between stacks.

## Q4: Agent Tool Capabilities
**Q:** Which PBA capabilities should Jarvis be able to invoke through voice/chat?

**A:** All four:
- Content pipeline (create, status, publish)
- Calendar (view, schedule, reschedule)
- Trends & analytics
- Social engagement (respond, opportunities)

## Q5: Monitor Alerts
**Q:** What jarvis-monitor alerts should PBA trigger?

**A:** All four:
- API health (critical if down)
- Content queue empty (medium)
- Post engagement anomaly (high)
- Pipeline failures (high)

## Q6: Briefing Integration
**Q:** Should Jarvis's morning briefing include PBA content status?

**A:** Yes, full summary — scheduled posts today, engagement highlights, trending topics, queue depth.

## Q7: Autonomy Model
**Q:** When Jarvis invokes PBA actions, should it require confirmation or act autonomously?

**A:** Respect PBA's autonomy dial. If PBA is set to auto-approve, Jarvis acts without confirmation. If manual, it queues for approval.

## Q8: Data Freshness for HUD
**Q:** Should the jarvis-hud PBA page show real-time updates or poll?

**A:** Real-time via SSE from PBA API. PBA pushes updates as content moves through pipeline.
