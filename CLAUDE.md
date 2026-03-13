# Personal Brand Assistant

## Overview
AI agent that manages all aspects of personal branding — social media posting, blog writing, content scheduling, and audience engagement.

## Stack
- **Backend:** .NET 9, C#, Minimal APIs or MediatR/CQRS
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
