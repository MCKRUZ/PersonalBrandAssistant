# Launch PBA locally with publishing secrets pulled from the DevSecrets vault at RUNTIME.
# Secrets are never written to .env or the repo — they live only in the vault and are injected
# as environment variables for this docker-compose run.
#
#   Prereq (one-time): store the secrets in the vault (Set-DevSecret prompts securely, no echo)
#     Import-Module C:\Users\kruz7\.devsecrets\DevSecrets.psm1
#     Set-DevSecret -Name LINKEDIN_CLIENT_ID       # paste app client id
#     Set-DevSecret -Name LINKEDIN_CLIENT_SECRET   # paste app client secret
#     Set-DevSecret -Name EXTERNAL_API_KEY         # paste a long random string
#   And a 256-bit token-encryption key (base64 32 bytes) — generate + store in one go:
#     $b=New-Object byte[] 32;[Security.Cryptography.RandomNumberGenerator]::Fill($b)
#     Set-DevSecret -Name PBA_ENCRYPTION_KEY -Secret ([Convert]::ToBase64String($b))
#
#   Run:  pwsh scripts/dev-up.ps1
$ErrorActionPreference = 'Stop'

Import-Module C:\Users\kruz7\.devsecrets\DevSecrets.psm1

foreach ($name in 'LINKEDIN_CLIENT_ID','LINKEDIN_CLIENT_SECRET','EXTERNAL_API_KEY') {
    $val = Get-DevSecret -Name $name
    if ([string]::IsNullOrWhiteSpace($val)) {
        throw "DevSecret '$name' is not set. Store it first (see header of this script)."
    }
    Set-Item -Path "env:$name" -Value $val   # inherited by docker compose, never persisted
}
# 256-bit token-encryption key (base64-encoded 32 bytes). The vault name differs from the .NET
# config env var, so it's fetched explicitly. Keep this key STABLE — changing it makes every
# already-stored OAuth token undecryptable.
$enc = Get-DevSecret -Name PBA_ENCRYPTION_KEY
if ([string]::IsNullOrWhiteSpace($enc)) {
    throw "DevSecret 'PBA_ENCRYPTION_KEY' is not set. Generate a base64 32-byte key and store it (see header)."
}
$env:Encryption__Key = $enc

# Non-secret config (a URL, safe to hardcode); must match the app's Authorized redirect URL.
$env:LINKEDIN_REDIRECT_URI = 'http://localhost:5001/api/auth/linkedin/callback'

Push-Location (Join-Path $PSScriptRoot '..')
try {
    docker compose up -d --build
} finally {
    Pop-Location
}

Write-Host ''
Write-Host 'PBA is up.' -ForegroundColor Green
Write-Host 'One-time LinkedIn consent -> open in your browser:' -ForegroundColor Cyan
Write-Host '  http://localhost:5001/api/auth/linkedin/authorize'
