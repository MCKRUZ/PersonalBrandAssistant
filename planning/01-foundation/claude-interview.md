# Deep Plan Interview — 01 Foundation

**Date:** 2026-03-13

## Q1: Architecture Pattern
**Question:** Clean Architecture vs Vertical Slices — which fits a modular agent system with 5 dependent splits?
**Answer:** Clean Architecture — strict layers, clear dependency direction.

## Q2: API Pattern
**Question:** Minimal APIs, MediatR/CQRS, or combination?
**Answer:** Minimal APIs + MediatR — Minimal API endpoints dispatching to MediatR handlers.

## Q3: Tenancy Model
**Question:** Single-user, multi-tenant, or single-user with hooks?
**Answer:** Single-user only. Simpler models, no tenant filtering. Can add later if needed.

## Q4: Content Entity Modeling
**Question:** TPH (single table), TPT (table per type), or TPC (table per concrete)?
**Answer:** Deferred to Claude's recommendation.
**Decision:** TPH (single table with discriminator). Rationale: workflow engine, analytics, and calendar all need to query across content types. Nullable columns are a small tradeoff vs. joins everywhere.

## Q5: OAuth Token Encryption
**Question:** ASP.NET Data Protection API vs custom AES-256-GCM?
**Answer:** ASP.NET Data Protection API — built-in, handles key rotation, keys stored on filesystem. Good fit for single-instance self-hosted deployment on Synology.

## Q6: Angular Scaffold Scope
**Question:** How much Angular work should foundation include?
**Answer:** Shell + design system — app shell with sidebar/header layout, lazy-loaded routes, plus shared component library and theming.

## Q7: UI Framework
**Question:** PrimeNG, Angular Material, or Tailwind + Headless UI?
**Answer:** PrimeNG — comprehensive component library (80+ components), built for Angular, includes themes.

## Q8: Synology NAS Specs
**Question:** CPU type and RAM for deployment target?
**Answer:** Intel/AMD, 8GB+ RAM — no constraints on Docker image choice or stack complexity.

## Summary of Decisions

| Decision | Choice |
|----------|--------|
| Architecture | Clean Architecture |
| API Pattern | Minimal APIs + MediatR |
| Tenancy | Single-user |
| Content Model | TPH (single table + discriminator) |
| Encryption | ASP.NET Data Protection API |
| Angular Scope | Shell + design system + PrimeNG |
| Synology | Intel x86, 8GB+ RAM |
