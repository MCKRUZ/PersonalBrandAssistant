---
paths: "**/Social/**,**/Integrations/**,**/Platforms/**"
---
# Social Media Integration Rules

- All platform integrations behind an abstraction (e.g., `ISocialPlatform`)
- OAuth tokens stored in encrypted storage, never in config files
- Implement rate limiting per platform (respect API quotas)
- Queue-based posting — never post synchronously from user requests
- All posts require approval workflow before publishing (unless auto-approve is explicitly enabled)
- Store post history for analytics and content reuse
