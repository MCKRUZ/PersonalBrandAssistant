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
*Last sync: 2026-04-16*

### Portfolio
| Project | Description | Tech |
|---------|------------|------|
| jarvis-stack | — | — |
| **personal-brand-assistant** (this) | — | — |
| project-avatar | — | — |
| ComfyUI | **ComfyUI** — the main local ComfyUI installation at E:/ComfyUI-Easy-Install/Co… | — |
| matthewkruczek-ai | **matthewkruczek.ai** — static personal brand website for Matthew Kruczek (EY M… | — |
| claude-code-mastery | **Claude Code Mastery** — the definitive Claude Code setup and configuration sk… | — |
| ComfyUI Expert | **VideoAgent / ComfyUI Expert** — session-scoped Claude Code orchestrator that … | — |
| Nexus | Nexus is a local-first cross-project intelligence layer for Claude Code. | — |
| _+31 inactive_ | — | — |

### Project Context
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
#### VoiceBox TTS — Sage Voice Generation (Jennifer Garner Clone)
## VoiceBox TTS for Sage

**App:** `C:\Users\kruz7\AppData\Local\Voicebox\voicebox.exe` (desktop app, auto-starts server)
**API:** `http://127.0.0.1:…
*Tags: voicebox, tts, voice-clone, jennifer-garner, sage-voice, api, furious*

#### Mac Mini SSH & Infrastructure
## Mac Mini (PRIMARY — Sage lives here)

- **Host:** 192.168.50.189
- **SSH:** `ssh matthewkruczek@192.168.50.189`
- **Platform:** Apple Silicon (arm…
*Tags: infrastructure, ssh, mac-mini, deployment, neo4j, docker*

> **Cross-project rule**: Before making decisions that affect shared concerns (APIs, auth, data formats, deployment) or asking the user for server/SSH/infrastructure details, run `nexus_query` to check for existing decisions, notes, and conflicts across the portfolio.

*[Nexus: run `nexus query` to search full knowledge base]*
<!-- nexus:end -->
