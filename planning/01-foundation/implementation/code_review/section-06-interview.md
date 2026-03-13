# Section 06 Docker - Review Interview (Autonomous Triage)

## Auto-Fix Items

1. **CRITICAL-1**: Add .dockerignore files (root + web) to prevent secret leakage
2. **HIGH-1**: Move DB port mapping to override file only (dev access)
3. **HIGH-3**: Fix chiseled image mkdir - create dir in build stage, COPY to runtime
4. **HIGH-4**: Repeat security headers in all nginx location blocks
5. **MEDIUM-1**: Simplify CSP connect-src to 'self' only
6. **MEDIUM-2**: Add commented HSTS header as reminder
7. **MEDIUM-4**: Add gzip_vary on
8. **MEDIUM-5**: Add server_tokens off
9. **LOW-2**: Remove deprecated X-XSS-Protection header
10. **LOW-3**: Clarify API_KEY comment in .env.example

## Let Go Items

- **HIGH-2**: ApiKey is the app's own auth key, not an external service key. Name is fine.
- **MEDIUM-3**: Volume mount path is standard docker-compose dev pattern, works correctly.
- **LOW-1**: Image pinning is premature for early-stage project.
- **LOW-4**: CI docs out of scope for this section.
