# Launch PBA locally with publishing secrets pulled from the DevSecrets vault at RUNTIME.
# Secrets are never written to .env or the repo — they live only in the vault and are injected
# as environment variables for this docker-compose run.
#
#   Prereq (one-time): store the secrets in the vault
#     Import-Module C:\Users\kruz7\.devsecrets\DevSecrets.psm1
#     Set-DevSecret -Name LINKEDIN_CLIENT_ID     -Value '<app client id>'
#     Set-DevSecret -Name LINKEDIN_CLIENT_SECRET -Value '<app client secret>'
#     Set-DevSecret -Name EXTERNAL_API_KEY       -Value '<a long random string>'
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
