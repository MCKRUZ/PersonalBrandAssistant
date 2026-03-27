# Interview Transcript: Autonomous LinkedIn Content Workflow

## Q1: ComfyUI Setup — Instance details, models, workflow templates?

**Answer:** User directed to check Nexus for infrastructure details.

**From Nexus:** ComfyUI runs on "Furious" workstation at `192.168.50.47:8188`. RTX 5090 GPU with 32GB VRAM. FLUX models available with identity-preserving LoRA models. Uses JSON API workflow format for programmatic invocation. Python orchestration scripts POST to `/prompt` and poll `/history` for results. Existing pattern: api_workflow JSON files for batch processing.

## Q2: Content type — Always LinkedIn SocialPost or vary by topic?

**Answer:** Vary by topic depth. AI decides: short topics → SocialPost, deeper topics → Thread or BlogPost excerpt.

## Q3: Review notification method in Semi-Auto mode?

**Answer:** Both dashboard + push notification. Content appears in review queue AND user gets pinged (Discord/email).

## Q4: Platform scope — LinkedIn only or multi-platform?

**Answer:** All connected platforms. The daily automation should publish to every connected platform with auto-formatting.

## Q5: Image prompt generation approach?

**Answer:** AI-generated prompts (Recommended). Claude analyzes the post content and generates a custom FLUX prompt each time — more creative, higher quality.

## Q6: Image generation failure fallback?

**Answer:** Block and notify. Don't publish without an image — hold for manual review and notify the user. Image is considered essential, not optional.

## Q7: Multi-platform content strategy?

**Answer:** Generate per platform. AI writes distinct versions: punchy tweet, professional LinkedIn post, blog teaser. Higher quality but more LLM calls. Each platform gets content tailored to its style and audience.

## Q8: Semi-Auto review window?

**Answer:** No auto-publish. In Semi-Auto mode, content always waits for explicit approval — never auto-publishes. No time-based expiry.

## Q9: Pipeline execution model for multi-platform?

**Answer:** All in one pipeline run. Generate all platform versions sequentially in the same orchestrator execution, not as separate background jobs.

## Q10: Image sharing across platforms?

**Answer:** One image, resize per platform. Generate a single image from ComfyUI, then auto-crop/resize for each platform's optimal dimensions (e.g., 1200x627 for LinkedIn, 1200x675 for Twitter, etc.).

## Q11: Topic selection strategy?

**Answer:** AI-curated selection. Send top 5 trends to Claude and let it pick the most compelling one based on engagement potential + topic diversity (avoids repetitive content).

---

## Summary of Key Decisions

| Decision | Choice |
|----------|--------|
| ComfyUI instance | Furious @ 192.168.50.47:8188, RTX 5090 32GB, FLUX models |
| Content type | AI-determined based on topic depth |
| Notification | Dashboard + push (Discord) |
| Platform scope | All connected platforms |
| Image prompts | AI-generated (Claude via sidecar) |
| Image failure | Block + notify (no publish without image) |
| Multi-platform content | Generate unique content per platform |
| Semi-Auto review | No auto-publish, explicit approval required |
| Pipeline model | All platforms in single run |
| Image strategy | One image, resize per platform |
| Topic selection | AI-curated from top 5 trends |
