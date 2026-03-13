# Section 01 Code Review Interview

## Auto-fixes Applied

1. **`.gitignore` `.env` pattern widened** — Changed from `.env` to `*.env` with `!.env.example` negation. Wider wildcard catches Docker-related env files (docker.env, production.env) coming in section-06.

## Noted Deviations (No Fix Needed)

1. **`.slnx` vs `.sln`** — .NET 10 SDK creates `.slnx` by default. Keeping modern format. Section doc will be updated.
2. **`coverlet.collector` in test projects** — Added by xunit template. Good addition for coverage. Will note in section doc.

## Items Let Go

None.
