$exePath = "C:\workstreams\codex-starfall-fishing\pr5-review\Builds\KookerStarfallIntegrated-0.0.11-survival.1-20261005-125920\KookerStarfallIntegrated.exe"
if (Test-Path -LiteralPath $exePath) {
    Write-Host "Launching Starfall Coastal Player (Round 332 - Swim Directive)..." -ForegroundColor Cyan
    Start-Process -FilePath $exePath
} else {
    Write-Error "Player executable not found at $exePath"
}
