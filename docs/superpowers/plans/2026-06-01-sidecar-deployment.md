# Sidecar Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the `pba-sidecar` container on the Mac Mini so PBA's LLM features (editor drafting, cross-post, voice-check) work end to end.

**Architecture:** Deployment-only. The compose `sidecar` service (build context `../claude-code-sidecar`, a Next.js server on :3001 that shells to `@anthropic-ai/claude-code` using mounted machine credentials) is built and brought up on the Mac Mini. No PBA application code changes. The `api` container is already wired with `Sidecar__WebSocketUrl=ws://sidecar:3001/ws`.

**Tech Stack:** Docker Compose, pnpm/turbo TypeScript monorepo, Claude Code CLI, Mac Mini (Apple Silicon, Docker Desktop), Tailscale SSH (`matthewkruczek@100.113.210.70`).

**Conventions for every command below:**
- Run over SSH from Furious: `ssh -o BatchMode=yes matthewkruczek@100.113.210.70 '<cmd>'`
- Prefix docker commands with `export PATH=/usr/local/bin:$PATH;` (docker is at `/usr/local/bin/docker`, not on the non-login PATH).
- Compose project dir: `~/personal-brand-assistant`.
- Ignore the noisy `cat: /Users/matthewkruczek/.keychain_pass: No such file or directory` line; it is harmless shell-profile output.

---

### Task 1: Diagnose and repair the sidecar repo on the Mac Mini

**Files:**
- Inspect: `~/claude-code-sidecar/Dockerfile`, `~/claude-code-sidecar/packages/{core,web}`
- Reference (known-good): Furious `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\claude-code-sidecar`

- [ ] **Step 1: Inventory the Mac Mini sidecar checkout**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'cd ~/claude-code-sidecar && echo DOCKERFILE: && ls -la Dockerfile 2>&1 && echo PACKAGES: && ls packages 2>&1 && echo LOCK: && ls -la pnpm-lock.yaml pnpm-workspace.yaml turbo.json tsconfig.base.json 2>&1 && echo GIT: && git remote -v 2>&1 | head -1 && git status --short 2>&1 | head -5'
```
Expected: `Dockerfile` present; `packages` lists `core` and `web`; lock/workspace files present. If any are **missing**, that explains the earlier `open Dockerfile: no such file or directory` build failure.

- [ ] **Step 2: If the checkout is incomplete, re-sync it**

Only if Step 1 showed missing files. Prefer re-cloning from the same git origin the Furious copy uses. First get the origin URL from Furious:
```
cd "C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\claude-code-sidecar" && git remote -v | head -1
```
Then on the Mac Mini, back up and re-clone (substitute `<ORIGIN_URL>`):
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'mv ~/claude-code-sidecar ~/claude-code-sidecar.bak.$(date +%s) && git clone <ORIGIN_URL> ~/claude-code-sidecar'
```
If the origin is not reachable from the Mac Mini (e.g. another bundle/local remote), instead `scp` the Furious tree excluding `node_modules`:
```
scp -r -o BatchMode=yes "C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\claude-code-sidecar/{Dockerfile,package.json,pnpm-lock.yaml,pnpm-workspace.yaml,turbo.json,tsconfig.base.json,packages,scripts,templates,sidecar.config.example.json}" matthewkruczek@100.113.210.70:~/claude-code-sidecar-stage/
```
(Adjust to land files at `~/claude-code-sidecar`.)

- [ ] **Step 3: Verify the build context resolves from the compose project**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'cd ~/personal-brand-assistant && ls -la ../claude-code-sidecar/Dockerfile'
```
Expected: the Dockerfile is listed (the compose `sidecar` build context is `../claude-code-sidecar`). This is the exact path Docker will read.

- [ ] **Step 4: Commit point (no code change)**

No repo commit — this task only repairs the deploy box. Record the outcome (was the checkout complete? what was re-synced?) in the task notes for the next task.

---

### Task 2: Authenticate Claude on the Mac Mini (USER-DRIVEN)

**Files:**
- Produces: `~/.claude/.credentials.json` on the Mac Mini

- [ ] **Step 1: Confirm Claude Code is available on the Mac Mini**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; which claude || npm ls -g @anthropic-ai/claude-code 2>/dev/null | head -2 || echo CLAUDE_CLI_ABSENT'
```
Expected: a path to `claude`, or evidence the global package is installed. If `CLAUDE_CLI_ABSENT`, install it: `ssh ... 'export PATH=/usr/local/bin:$PATH; npm install -g @anthropic-ai/claude-code'`.

- [ ] **Step 2: Check for existing credentials**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'ls -la ~/.claude/.credentials.json 2>&1'
```
Expected: currently `No such file or directory` (verified 2026-06-01). If it already exists, skip to Step 4.

- [ ] **Step 3: USER runs the Claude login on the Mac Mini**

This step is performed by the user, not the agent (OAuth/browser flow). Two options:
- **If the user has a GUI/screen-share to the Mac Mini:** run `claude` and complete the in-app login.
- **If headless:** run `claude setup-token` in an interactive SSH session and follow the device-code/URL prompt in a browser on any machine.

Agent waits for the user to confirm completion. Tell the user verbatim what to run:
```
ssh matthewkruczek@100.113.210.70
claude setup-token   # or: claude   (then /login)
```

- [ ] **Step 4: Verify credentials exist and Claude runs non-interactively**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; ls -la ~/.claude/.credentials.json && echo "2+2" | claude -p 2>&1 | tail -3'
```
Expected: the credentials file exists and a non-interactive `claude -p` prompt returns a response (proves the token works). If it errors with an auth message, repeat Step 3.

---

### Task 3: Build the sidecar image

**Files:**
- Uses: `~/claude-code-sidecar/Dockerfile`
- Compose: `~/personal-brand-assistant/docker-compose.yml:20-29`

- [ ] **Step 1: Build only the sidecar service**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; cd ~/personal-brand-assistant && docker compose build sidecar 2>&1 | tail -25'
```
Expected: build proceeds through the pnpm install, `tsc` core build, `next build`, and the production stage that runs `npm install -g @anthropic-ai/claude-code`. Final line indicates success (no `failed to solve` / `no such file` errors).

- [ ] **Step 2: Confirm the image exists**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; docker images | grep -i sidecar'
```
Expected: a `personal-brand-assistant-sidecar` (or compose-named) image is listed.

- [ ] **Step 3: If the build fails, capture the full error**

Run with full output (not tail) and stop to diagnose before proceeding:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; cd ~/personal-brand-assistant && docker compose build sidecar 2>&1' | tee /tmp/sidecar-build.log
```
Common causes: incomplete checkout (return to Task 1), or Apple-Silicon/arm64 base-image issue (the Dockerfile uses `node:22-alpine`, which is multi-arch — should be fine).

---

### Task 4: Bring up the sidecar container

**Files:**
- Compose service: `sidecar` (mounts in `docker-compose.override.yml:6-15`: creds, CLAUDE.md, rules; ports 3001)

- [ ] **Step 1: Verify the mounted host paths exist**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'for p in ~/.claude/.credentials.json ~/.claude/CLAUDE.md ~/.claude/rules ~/personal-brand-assistant/prompts/sidecar.config.json; do [ -e "$p" ] && echo "OK $p" || echo "MISSING $p"; done'
```
Expected: all `OK`. `~/.claude/.credentials.json` should now exist from Task 2. If `~/.claude/CLAUDE.md` or `~/.claude/rules` are missing, the read-only mounts will fail; create/sync them or the container will error on start.

- [ ] **Step 2: Start the sidecar (do NOT touch api)**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; cd ~/personal-brand-assistant && docker compose up -d --no-deps sidecar 2>&1 | tail -8'
```
Expected: `Container pba-sidecar Started`. `--no-deps` protects the running `api` (live LinkedIn creds) from recreation.

- [ ] **Step 3: Confirm it is running and healthy**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; docker ps --format "{{.Names}} {{.Status}} {{.Ports}}" | grep pba-sidecar; echo LOGS:; docker logs pba-sidecar --tail 15 2>&1 | tail -15'
```
Expected: `pba-sidecar` `Up`, listening on 3001; logs show the Next.js server started with no auth/credential errors.

---

### Task 5: Verify api↔sidecar connectivity

- [ ] **Step 1: api resolves the sidecar host**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; docker exec pba-api sh -c "getent hosts sidecar || nslookup sidecar 2>/dev/null" | head -2'
```
Expected: an IP for `sidecar` (previously `NO_SIDECAR_HOST`). If still unresolved, the services are not on the same compose network — check `docker network inspect` and the override `networks:` for sidecar (lines 13-15).

- [ ] **Step 2: api can reach the sidecar port**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; docker exec pba-api sh -c "wget -q -T5 -O- http://sidecar:3001/ 2>&1 | head -c 120 || echo CONNECT_FAIL"'
```
Expected: an HTTP response body (any 2xx/3xx/404 from the Next.js app), not `CONNECT_FAIL`. Confirms reachability of `ws://sidecar:3001/ws`'s host:port.

---

### Task 6: End-to-end verification (the real acceptance test)

- [ ] **Step 1: Identify a content id to draft against**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; curl -s "http://localhost:5001/api/content?page=1&pageSize=1" | head -c 300'
```
Expected: a content list. Note an `id`, or create one via `POST /api/content` (Title, ContentType=BlogPost, PrimaryPlatform=Blog) if the list is empty.

- [ ] **Step 2: Trigger a draft and confirm LLM text returns through the sidecar**

Use the editor's draft path (`POST /api/content/{id}/draft` with `{ "action": "...", "instructions": "Draft a one-paragraph test." }` — confirm the exact body shape against `DraftContent.Command` before running). Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; curl -s -X POST "http://localhost:5001/api/content/<ID>/draft" -H "Content-Type: application/json" -d "{\"action\":\"DraftFromScratch\",\"instructions\":\"Write one test sentence.\"}" | head -c 400; echo; echo SIDECAR_LOG:; docker logs pba-sidecar --tail 8 2>&1 | tail -8'
```
Expected: a success result containing generated text, and the sidecar log shows it handled the request. This proves the full chain: api → sidecar → Claude → back. **This is the acceptance criterion.**

- [ ] **Step 3: Confirm in the UI (optional but recommended)**

With the web tunnel open (`ssh -L 4201:localhost:4201 matthewkruczek@100.113.210.70`), open a content item at `http://localhost:4201/content/<id>`, use sidecar-chat "Draft from scratch," and confirm text streams back.

---

### Task 7: Ensure the sidecar survives reboots

**Files:**
- Modify (if needed): `~/personal-brand-assistant/docker-compose.override.yml` (sidecar service) and the repo copy

- [ ] **Step 1: Check the current restart policy**

Run:
```
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'export PATH=/usr/local/bin:$PATH; docker inspect pba-sidecar --format "{{.HostConfig.RestartPolicy.Name}}"'
```
Expected: ideally `unless-stopped` or `always`. If it prints `no` or empty, add a restart policy.

- [ ] **Step 2: If missing, add `restart: unless-stopped` to the sidecar service**

Edit the repo `docker-compose.override.yml` sidecar service (Furious) to add `restart: unless-stopped`, mirroring whatever `api`/`db` use. Then sync to the Mac Mini via git (it is now reconnected to GitHub):
```bash
# Furious
git add docker-compose.override.yml && git commit -m "chore: add restart policy to sidecar service"
git push origin v2-rebuild && git checkout main && git merge --ff-only v2-rebuild && git push origin main && git checkout v2-rebuild
```
```
# Mac Mini
ssh -o BatchMode=yes matthewkruczek@100.113.210.70 'cd ~/personal-brand-assistant && git pull --ff-only && export PATH=/usr/local/bin:$PATH && docker compose up -d --no-deps sidecar'
```

- [ ] **Step 3: Final verification**

Run Task 6 Step 2 once more after the policy change to confirm the sidecar still drafts. Update `MEMORY.md` / `project_mac_mini_deployment` to record the sidecar is deployed and AI features are live.

---

## Self-Review

- **Spec coverage:** Spec approach steps 1-5 map to Tasks 1 (repair repo), 2 (auth), 3 (build), 4 (bring up), 5+6 (verify). Spec risk "no auto-restart guarantee" → Task 7. All acceptance criteria (`pba-sidecar` healthy, `getent hosts sidecar` resolves, draft returns text) are covered by Tasks 4-6.
- **Placeholder scan:** `<ORIGIN_URL>` and `<ID>` are intentional runtime substitutions with explicit commands to obtain them, not vague TODOs. The draft request body is flagged to confirm against `DraftContent.Command` before running.
- **Consistency:** Container names (`pba-sidecar`, `pba-api`), the `export PATH` prefix, and the SSH host are consistent across all tasks.
- **Known gap to resolve at execution:** Task 6 Step 2's exact `/draft` request shape must be confirmed against `DraftContent.Command` (`src/PBA.Application/Features/Content/Commands/`) and `ContentEndpoints.cs:77-82` — the `action`/`instructions`/`toneName` fields — before issuing the call.
