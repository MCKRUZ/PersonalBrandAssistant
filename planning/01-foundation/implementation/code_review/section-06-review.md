# Section 06 Docker - Code Review

## CRITICAL-1: Missing .dockerignore files - WILL FIX
## HIGH-1: PostgreSQL port exposed in production - WILL FIX
## HIGH-2: ApiKey generic name - LET GO (this is the app's API key, not Anthropic's)
## HIGH-3: Chiseled image can't run mkdir - WILL FIX
## HIGH-4: Security headers lost in nginx location blocks - WILL FIX
## MEDIUM-1: CSP connect-src internal hostname - WILL FIX
## MEDIUM-2: Missing HSTS comment - WILL FIX
## MEDIUM-3: Volume mount path mismatch - LET GO (works fine, standard pattern)
## MEDIUM-4: Missing gzip_vary - WILL FIX
## MEDIUM-5: Missing server_tokens off - WILL FIX
## LOW-1: Pin image digests - LET GO (early stage)
## LOW-2: X-XSS-Protection deprecated - WILL FIX (remove it)
## LOW-3: .env.example API_KEY docs - WILL FIX
## LOW-4: CI build docs - LET GO (not in scope)
