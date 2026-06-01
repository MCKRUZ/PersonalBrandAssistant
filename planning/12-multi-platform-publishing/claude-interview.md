# Multi-Platform Publishing Interview

## Q1: Substack Integration Approach

**Question:** For Substack (no official API), research shows two viable approaches: (1) Direct HTTP with reverse-engineered internal API and cookie-based auth, or (2) Run the Python Substack-API library as a Docker sidecar and call it from .NET. Which approach?

**Answer:** Direct HTTP in C# — port the reverse-engineered API calls to HttpClient with cookie management. Self-contained, no extra container, but brittle.

## Q2: OAuth Token Storage

**Question:** For OAuth tokens (LinkedIn, Twitter), where should we store them? These are long-lived credentials that need to survive restarts and be refreshable.

**Answer:** Database (encrypted column) — store encrypted tokens in PostgreSQL alongside content data. Simplest, all in one place.

## Q3: Multi-Platform Failure Handling

**Question:** When publishing to multiple platforms simultaneously, how should failures be handled?

**Answer:** Primary + best-effort — primary platform (Blog) must succeed. Others are best-effort with retry queue.

## Q4: Connector Interface Design

**Question:** Should we keep IBlogConnector as-is and add parallel interfaces, or refactor to a single IPlatformConnector that all platforms implement with keyed DI by Platform enum?

**Answer:** Single IPlatformConnector + keyed DI — one interface, factory resolves by Platform enum. BlogConnector adapts to new interface. Clean, extensible.

## Q5: Existing API Credentials

**Question:** Do you have existing API credentials/tokens for any of these platforms?

**Answer:** Have LinkedIn developer app. Need to set up everything else (Medium token, Twitter/X developer app).

## Q6: Content Transformation Architecture

**Question:** Should we build transformations as a pipeline with per-platform formatters, or keep transformation logic inside each connector?

**Answer:** Shared pipeline + formatters — central IContentTransformer pipeline with pluggable IPlatformFormatter per platform. More structured.

## Q7: Frontend Publishing UX

**Question:** How should platform publishing controls look in the Content Editor?

**Answer:** Both — set default target platforms on content creation/editing, but show a confirmation modal at publish time to adjust before firing.

## Q8: OAuth Flow Timing

**Question:** Should we build a full OAuth callback flow now, or stub with manual token entry first?

**Answer:** Build OAuth flow now — full OAuth callback endpoint, token exchange, refresh logic. Complete implementation.
