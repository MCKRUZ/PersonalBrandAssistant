# Sidecar Deployment — Design

**Date:** 2026-06-01
**Status:** Proposed
**Sub-project:** #1 of the "PBA full autonomous blog publish" program

## Context

The user wants PBA to fully own the blog publish pipeline that the
`matt-kruczek-blog-writer` skill performs against the `matthewkruczek-ai`
website repo today ("full autonomous publish"). That program decomposes into
six dependency-ordered sub-projects:

1. **Deploy the sidecar** (this spec) — gating dependency for all LLM features
2. Wire idea→content (`onCreateContent()` is a section-16 stub)
3. Real SEO blog HTML generation (replace placeholder `BlogFormatter`)
4. Site index manipulation (`blog.html`, `index.html`, `sitemap.xml`)
5. Hero image generation (ComfyUI → `assets/blog-images/{slug}.png`)
6. Git publish pipeline (website repo checkout + push creds → Netlify)

This sub-project unblocks #1. Nothing in PBA can draft, cross-post, or
voice-check without the sidecar, because **all LLM calls route through
`ISidecarClient`** (project rule), never the Anthropic SDK directly.

## Problem

On the Mac Mini deploy (`100.113.210.70`, Tailscale), there is **no
`pba-sidecar` container**. The `api` container is configured with
`Sidecar__WebSocketUrl=ws://sidecar:3001/ws` but cannot resolve the `sidecar`
host (`getent hosts sidecar` → no host). As a result, the content editor's
"Draft from scratch" and all other AI features fail.

## Current state (verified 2026-06-01)

- Compose `sidecar` service build context: `../claude-code-sidecar`
  (`docker-compose.yml:20-29`), a separate sibling repo.
- `~/claude-code-sidecar` **is present** on the Mac Mini (pnpm + turbo TS
  monorepo: `packages/core`, `packages/web`; Next.js server on port 3001).
- A prior `docker compose up -d sidecar` failed with
  `failed to read dockerfile: open Dockerfile: no such file or directory` —
  cause unconfirmed; the Dockerfile exists in the Furious copy of the repo.
- `~/.claude/.credentials.json` is **absent** on the Mac Mini.
- The sidecar Dockerfile installs `@anthropic-ai/claude-code` and, per the
  sidecar's own `CLAUDE.md:42`, **never manages auth — it reuses existing
  machine credentials**. The override mounts
  `~/.claude/.credentials.json`, `~/.claude/CLAUDE.md`, `~/.claude/rules`
  read-only into the container (`docker-compose.override.yml:9-12`).

## Decision

Generate Claude credentials **natively on the Mac Mini** via the Claude Code
login (chosen over copying `.credentials.json` from another machine — cleaner
provenance, the token belongs to the box that uses it).

## Approach

1. **Verify/repair the sidecar repo on the Mac Mini.** Confirm
   `~/claude-code-sidecar` is a complete checkout (Dockerfile, `pnpm-lock.yaml`,
   `packages/core`, `packages/web`). If incomplete, re-sync from the Furious
   copy or its git origin. Confirms the cause of the earlier build failure.
2. **Authenticate Claude on the Mac Mini.** Run the Claude Code login so
   `~/.claude/.credentials.json` is produced. If the box is headless, use
   `claude setup-token` or a screen-sharing session; **the user drives the
   actual OAuth step.** Verify the file exists and `claude` runs non-interactively.
3. **Build the sidecar image.** `docker compose build sidecar` from
   `~/personal-brand-assistant` (context resolves to `../claude-code-sidecar`).
   Resolve any build-context/Dockerfile path issue surfaced earlier.
4. **Bring up the container.** `docker compose up -d sidecar`. The override
   already mounts creds + `CLAUDE.md` + `rules` and joins the compose network.
   Do **not** recreate `api` unless needed (protect the live LinkedIn creds).
5. **Verify end to end.**
   - `api` resolves `sidecar` host and the websocket connects.
   - In the content editor, "Draft from scratch" returns generated text.
   - Cross-post generation and voice-check succeed.

## Components touched

No PBA application code changes. This is deployment work against the existing
compose `sidecar` service. The `api` is already wired to consume it.

## Acceptance criteria

- `docker ps` shows `pba-sidecar` healthy.
- `docker exec pba-api getent hosts sidecar` resolves.
- A draft request from the PBA content editor returns LLM-generated text
  (the end-to-end proof, not just container health).

## Risks / unknowns

- **Headless Claude login.** The Mac Mini may have no browser session over SSH;
  the OAuth flow may require `claude setup-token` or screen sharing. User-driven.
- **Earlier Dockerfile build failure.** Root cause unconfirmed (incomplete
  repo checkout vs. context path). Step 1 must resolve it before step 3.
- **Quota.** The sidecar runs on the user's Claude subscription; AI features
  consume that quota.
- **No auto-restart guarantee.** Confirm the sidecar service has an appropriate
  `restart` policy so it survives Mac Mini reboots.

## Out of scope

Blog-specific subsystems (#2–6). Each gets its own design → plan → implement
cycle after the sidecar is live and verified.
