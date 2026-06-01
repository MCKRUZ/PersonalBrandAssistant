# Personal Brand Assistant

## Overview
AI agent that manages all aspects of personal branding — social media posting, blog writing, content scheduling, and audience engagement.

## Stack
- **Backend:** .NET 10, C#, Minimal APIs or MediatR/CQRS
- **Frontend:** Angular 19, standalone components, NgRx signals
- **AI/LLM:** Claude API (Anthropic SDK), agent orchestration
- **Database:** TBD (likely SQL Server or PostgreSQL)
- **Deployment:** TBD

## Architecture
Early stage — architecture not yet defined. Use `/deep-project` to decompose requirements before implementation.

## Commands
- Backend: `dotnet build`, `dotnet test`, `dotnet run`
- Frontend: `npm install`, `ng serve`, `ng test`, `ng build`
- Full verify: `dotnet build && dotnet test && cd frontend && ng build`

## Code Style
Follow global rules in `~/.claude/rules/coding-style.md`:
- Immutable patterns everywhere (spread ops, records, `with` expressions)
- Small files (200-400 lines), organized by feature/domain
- Result<T> pattern for error handling in C#
- Reactive Forms + FluentValidation for input validation

## Task Approach
1. This project is in early ideation — prefer planning over premature implementation
2. When adding features, use `/deep-plan` for anything touching 3+ files
3. AI agent logic should be modular — each "capability" (social posting, blog writing, etc.) as an independent module
4. All LLM calls go through a central orchestration layer, never directly from controllers/components

## Security
- API keys and secrets: User Secrets (dev), Azure Key Vault (prod)
- Never store social media tokens in code — use encrypted storage
- Rate limit all public endpoints
- OAuth flows for social media integrations (Twitter/X, LinkedIn, etc.)

<!-- nexus:start -->
## Nexus Intelligence

*Auto-updated by Nexus — do not edit this section manually.*
*Last sync: 2026-06-01*

### Portfolio
| Project | Description | Tech |
|---------|------------|------|
| ai-video-producer | — | — |
| **personal-brand-assistant** (this) | — | — |
| project-avatar | — | — |
| matthewkruczek-ai | **matthewkruczek.ai** — static personal brand website for Matthew Kruczek (EY M… | — |
| claude-code-mastery | **Claude Code Mastery** — the definitive Claude Code setup and configuration sk… | — |
| Nexus | Nexus is a local-first cross-project intelligence layer for Claude Code. | — |
| _+34 inactive_ | — | — |

### Project Context
#### Deployment: Local Docker on Mac Mini
## PBA Deployment

- **Host:** Mac Mini (192.168.50.103)
- **Runtime:** Docker Compose
- **Branch deployed:** v2-rebuild
- **Platform:** Apple Silico…
*Tags: deployment, docker, mac-mini, infrastructure, tailscale*

#### Deployment: Local Docker on Furious
## PBA Deployment

- **Host:** Furious (local machine)
- **Runtime:** Docker Compose
- **Branch deployed:** main
- **Last known deploy:** manual, nee…
*Tags: deployment, docker, furious, infrastructure*

#### Website Analytics: Google Search Console + GA4
## Website Analytics for matthewkruczek.ai

### Service Account Credentials
- **File:** `secrets/google-analytics-sa.json` (gitignored)
- **Email:** …
*Tags: analytics, google-search-console, ga4, matthewkruczek-ai, seo, website-stats*

### Context from project-avatar
#### Mac Mini SSH & Infrastructure
## Mac Mini (PRIMARY — Sage lives here)

- **Host:** 192.168.50.103
- **SSH:** `ssh matthewkruczek@192.168.50.103`
- **Platform:** Apple Silicon (arm…
*Tags: infrastructure, ssh, mac-mini, deployment, neo4j, docker, pba*

#### Civitai API Key
## Civitai API Key

**Key:** `f3c2926aa7fcc3919915fd3a079b7f21`

**Use:** Civitai model downloads via API (`https://civitai.com/api/download/models/{…
*Tags: civitai, api-key, credentials, download, lora*

#### VoiceBox TTS — Sage Voice Generation (Jennifer Garner Clone)
## VoiceBox TTS for Sage

**App:** `C:\Users\kruz7\AppData\Local\Voicebox\voicebox.exe` (desktop app, auto-starts server)
**API:** `http://127.0.0.1:…
*Tags: voicebox, tts, voice-clone, jennifer-garner, sage-voice, api, furious*

### Context from ai-video-producer
#### Bifrost MCP Gateway — Docker Setup & Credentials
## Bifrost MCP Gateway (Docker on Mac Mini)

**Host:** Mac Mini (192.168.50.189)
**Path:** ~/bifrost/
**Container:** bifrost (from maximhq/bifrost:la…
*Tags: bifrost, mcp, docker, credentials, mac-mini, infrastructure*

#### Qwen3-Omni-30B-A3B — Local vLLM Server on Furious
## Qwen3-Omni-30B-A3B on Furious

**Host:** Furious (local Windows 11, RTX 5090 32GB)
**Runtime:** Docker CE 29.5.0 in WSL Ubuntu (not Docker Desktop…
*Tags: qwen3-omni, vllm, furious, local-llm, multimodal, docker, gpu, infrastructure*

### Recorded Decisions
- **[workflow]** Use git commit trails with structured review documentation for each section
  > Workflow creates section-specific code review interview files and commits with detailed messages documenting review findings and architectural decisions
- **[workflow]** Use project memory files (project_v2_rebuild_status.md, MEMORY.md) to track mul…
  > Enables continuation of long-running projects across multiple Claude sessions by maintaining human-readable status and next steps
- **[integration]** Integrate FreshRSS for content aggregation
  > Enables RSS feed integration capabilities within the idea bank feature
- **[security]** Enforce hostname validation via `allowedHosts` config in Angular dev server
  > Prevent host header injection attacks; required when exposing dev server through external URLs like Tailscale Funnel
- **[security]** Include article URLs in IdeaDto API response for clickable content
  > Provides direct access to source material without additional service calls; enables transparent content tracing

### Active Conflicts
- [medium] personal-brand-assistant uses SignalR for real-time feed notifications while jarvis-stack enforces OpenRouter API (no Anthropic models) — if personal-brand-assistant's notifications embed LLM calls, the tiered model strategy incompatibility creates maintenance friction in shared deployment.

> **Cross-project rule**: Before making decisions that affect shared concerns (APIs, auth, data formats, deployment) or asking the user for server/SSH/infrastructure details, run `nexus_query` to check for existing decisions, notes, and conflicts across the portfolio.
> When the user says "save to nexus" or you learn something worth preserving across sessions (project identity, key entities, architecture decisions, cross-project relationships), call `nexus_note` with `action: "set"` and a descriptive `title`.

*[Nexus: `nexus_query` to search | `nexus_note` to save | `nexus_decide` to record decisions]*
<!-- nexus:end -->
