param(
    [string]$Player = 'Builds/KookerStarfallIntegrated-0.0.11-survival.1-20260918-112206/KookerStarfallIntegrated.exe',
    [int]$TimeoutSec = 120
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$playerPath = (Resolve-Path -LiteralPath (Join-Path $project $Player)).Path
$dir = Join-Path $project 'evidence/local/integrated-acceptance-test'
if (Test-Path -LiteralPath $dir) { Remove-Item -Recurse -Force $dir }
$null = New-Item -ItemType Directory -Path $dir

$args = @('-batchmode', '-force-d3d11', '-integratedSmoke', '-integratedEvidence', $dir, '-logFile', (Join-Path $dir 'player.log'))
Write-Output "Launching standalone player: $playerPath"
$proc = Start-Process -FilePath $playerPath -ArgumentList $args -PassThru
$watch = [Diagnostics.Stopwatch]::StartNew()
$finished = $proc.WaitForExit($TimeoutSec * 1000)
$watch.Stop()

if (-not $finished) {
    $proc.Kill()
    Write-Warning "Player timed out after $TimeoutSec seconds"
}

Write-Output "Player exit code: $($proc.ExitCode) in $([Math]::Round($watch.Elapsed.TotalSeconds, 1))s"
$runtimeJson = Join-Path $dir 'integrated-runtime.json'
if (Test-Path -LiteralPath $runtimeJson) {
    Get-Content -LiteralPath $runtimeJson -Raw | Out-Host
} else {
    Write-Output "player.log tail:"
    Get-Content (Join-Path $dir 'player.log') -Tail 40 | Out-Host
}
