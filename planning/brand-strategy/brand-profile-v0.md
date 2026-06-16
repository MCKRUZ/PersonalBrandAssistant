# Brand Profile v0 — inferred draft (for red-pen)

Seeded by inference from: 60 published posts (matthewkruczek.ai), 3 voice skills,
the AI Pioneer Strategy doc, 60+ created GitHub repos, 1yr of starred repos.
Every claim cites its evidence. This becomes the ranker's source of truth once you edit it.

---

## Positioning (north star — replaces the hardcoded sentence)

> **"I show enterprise teams what AI can actually ship — by building it myself."**

*Evidence:* Verbatim from the strategy doc; validated by the credibility triangle —
Big-4 scale (MD at EY, 150+ team) + hands-on builder (60+ repos, DotNetSkills = first C#
Agent Skills impl) + consistent publisher (60 long-form posts). Few people sit at all three corners.

## Audience

- **Primary:** Senior engineers, EMs, architects at enterprise companies — frustrated by AI
  hype, want proof, patterns, shipped artifacts. *(strategy doc + LinkedIn skill: "practitioners who respect specifics")*
- **Secondary:** Enterprise executives / decision-makers. *(every blog opens with an "Executive read"; voice = "simple enough for execs, technical enough to not be fluff")*

## Content Pillars (weighted — these are what each item is scored against)

| # | Pillar | Weight | What it is | Evidence |
|---|--------|-------:|-----------|----------|
| 1 | **Agent-Native Architecture** | **0.28** | Harnesses, Agent Skills, MCP, context engineering, agent memory, multi-agent patterns, plus AI economics/tokenomics/cost | ~23 posts (agent-skills-missing-link, building-agentic-harness, neurosymbolic-harness, the-missing-layer, mcp-skills-a2a-three-layers, progressive-disclosure-mcp-servers, context-engineering, agentic-memory, tokenomics-beyond-the-cap, toon-token-optimization…). Heaviest build proof: microsoft-agentic-harness, DotNetMCPServer, mcp-hub, mck-scaffold, llm-cost-optimizer-skill |
| 2 | **Enterprise AI Adoption & Governance** | **0.23** | Exec-altitude: strategy, operating model, governance, ROI, the 95%-failure framing | ~14 posts (ai-adoption-paradox, ai-operating-model, enterprise-ai-governance, apim-agentic-governance, governed-ai-at-speed, ai-readiness-scorecard, measuring-agentic-ai-impact). Stars: arc-kit, microsoft/agent-governance-toolkit. Repos: consultant-helper, ey-executive-skill |
| 3 | **Agentic SDLC & Eng Transformation** | **0.22** | How engineering orgs actually build with agents; orchestrate-not-implement; training | ~11 posts (transforming-sdlc-with-ai, training-engineers-orchestrate, agentic-engineering-org, agents-changing-software-development, app-modernization-sdlc, scaling-pilot-to-production). Builds: claude-code-sdlc, intent-driven-development, agentic-sdlc-hackathon, developer-overwatch, SDLCOrchestrator |
| 4 | **Claude / Anthropic Agent Engineering** | **0.15** | Bringing Anthropic/Claude-Code patterns into the enterprise; Agent Skills, Claude Code architecture, the MD-who-ships-on-Claude position | Deepest hands-on lane by build volume: DotNetSkills (first C# port of Anthropic Agent Skills), claude-code-sdlc, claude-code-mastery, claude-config, claude-model-switcher, dozens of Claude Code skills. Nearly all 1yr stars are Claude-Code-ecosystem tooling |
| 5 | **Microsoft Enterprise AI Stack** | **0.12** | Foundry, Copilot, Agent Framework, .NET/C# in enterprise AI | ~6 posts (computer-using-agents-foundry, copilot-enterprise-value, microsoft-ai-studios-comparison, multi-agent-patterns-microsoft). Authority: MS Inner Circle (AI & Entra), MVP application, csharp-python/csharp-java-enterprise-ai |

*Weights sum to 1.0. Rationale: pillars 1–3 are the spine (0.73 combined). Pillar 4 (Anthropic)
is elevated to its own lane — by build volume it's Matt's deepest hands-on area, and the
"Microsoft MD who builds the enterprise on Claude" tension is the differentiator. AI Economics
folded into pillar 1.*

## Authority Topics (build-earned — score multiplier ×1.2 when an item hits these)

Narrow areas where shipped code earns Matt a strong take:
- **.NET / C# in enterprise AI** — DotNetSkills (first C# Agent Skills impl), DotNetMCPServer, DotNetSkills executor
- **Anthropic Agent Skills / Claude Code architecture** — DotNetSkills (first C# port of Anthropic's framework), claude-code-mastery, claude-config, claude-model-switcher
- **Claude-Code-style agent harnesses** — microsoft-agentic-harness, mck-scaffold, claude-code-sdlc
- **Agent Skills framework** — DotNetSkills, multiple skill repos published
- **MCP server design** — mcp-hub, DotNetMCPServer, progressive-disclosure posts
- **Agentic SDLC tooling** — claude-code-sdlc, developer-overwatch, intent-driven-development

## Anti-topics (looks-relevant-but-off-brand — score multiplier ×0.1)

- **Digital-human / avatar / talking-head tech** (LivePortrait, SadTalker, EchoMimic, avatars, voice-clone) — this is **project-avatar/Sage**, a separate interest, NOT brand content. *Largest false-positive risk in the star data.*
- **Consumer-AI gossip / product drama** (ChatGPT feature wars, who-said-what)
- **Model-release horse-race / benchmark-leaderboard news** as primary topic
- **Crypto / web3 / AI-token coins**
- **AGI philosophy / doomerism** (not the enterprise-pragmatist lane)
- **Funding-round / VC news** with no enterprise-build angle
- **Pure consumer dev tutorials** with no enterprise constraint (security/scale/compliance/cost)

## Voice markers (for "could I write this in my voice")

- Simple enough for execs, technical enough to not be fluff
- Contrarian / false-debate debunking ("here's where everyone is wrong")
- Data > opinion — specific numbers ("85-100x token reduction"), not "significant improvements"
- **The Mollick rule:** every claim has a build behind it. No abstract hot takes.
- No em dashes, no AI-isms (delve/crucial/leverage/robust/landscape), no promotional inflation

## Recency

- Half-life: **7 days** (configurable). Thought-leadership, not breaking news.

---

## Red-pen resolution (2026-06-15 — agreed)

- Avatar/talking-head = anti-topic. **Confirmed.**
- Anthropic elevated to its own pillar (0.15) + added as authority topic. **Confirmed.**
- AI Economics folded into Agent-Native Architecture; stays at 5 pillars. **Confirmed.**
- Weights rebalanced to 0.28 / 0.23 / 0.22 / 0.15 / 0.12 (sum 1.0). **Confirmed.**

Profile is **agreed (v1)**. This is the seed the ranker scores against. Editable later via the
Brand Profile UI (weights auto-apply; pillar-definition edits trigger a re-score).
