# Blog Publishing Workflow: Substack-First with Delayed Blog Republish

## Problem

We need a content pipeline for creating long-form blog posts that publish to Substack first, then automatically republish to the personal blog (matthewkruczek.ai) after a configurable delay (default 7 days). Currently, Substack and PersonalBlog exist as platform enum values but have no publishing adapters, and there's no concept of staggered/delayed cross-platform publishing.

## Goals

1. **Substack publishing adapter** - Substack has no public write API, so PBA should prepare fully formatted content for manual publish (copy-to-clipboard with Substack-optimized formatting), then auto-detect publication via the existing RSS feed integration to confirm publish and trigger downstream scheduling.

2. **PersonalBlog publishing adapter** - Generate HTML using the matt-kruczek-blog-writer skill conventions, then commit to the matthewkruczek-ai repo and trigger deployment. This should be automatable end-to-end.

3. **Staggered publish scheduling** - Parent-child content relationships with temporal dependencies. When a Substack post is published (detected via RSS), the PersonalBlog child content should automatically schedule itself for X days later (default 7). The delay should be configurable per content piece.

4. **Cross-platform scheduling rules** - Enforce platform publish ordering. For the blog workflow: Substack must publish before PersonalBlog. The system should block/warn if someone tries to publish the blog version before the Substack version is live.

5. **Content page UX** - Clear UI showing the two-stage publish pipeline: Substack status (draft/ready/published) and Blog status (waiting/scheduled/published) with visual timeline. The user should see at a glance where each blog post is in the pipeline.

## Existing Infrastructure

- Content entity with full state machine (Draft -> Review -> Approved -> Scheduled -> Publishing -> Published)
- RepurposingService with parent-child content relationships and tree depth tracking
- PublishingPipeline with platform adapters (Twitter/X, LinkedIn, Instagram, YouTube, Reddit implemented)
- SubstackService (RSS read-only, analytics)
- Content pipeline UI (Topic -> Outline -> Draft -> Review wizard)
- Content calendar service for scheduling
- PlatformType enum includes Substack and PersonalBlog
- ContentType enum includes BlogPost

## Constraints

- Substack has no write API - publishing must remain a manual step with PBA assistance
- matthewkruczek.ai is a static site - blog publishing means generating HTML and committing to the repo
- The RSS feed poll interval determines how quickly we detect Substack publication
- Must work within existing content state machine and repurposing patterns
- Content formatting differs between Substack (markdown/rich text) and the blog (HTML template)

## Out of Scope

- Substack API integration (doesn't exist)
- Changes to the matthewkruczek.ai site structure itself
- Social media promotion of blog posts (separate feature)
- Analytics/engagement tracking for blog posts (existing analytics dashboard handles this)
