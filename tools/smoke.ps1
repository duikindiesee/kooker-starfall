param(
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$BuildName = 'Candidate'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$player = Join-Path $projectRoot ('Builds\' + $BuildName + '\CityLife.exe')
if (-not (Test-Path -LiteralPath $player)) { throw 'Build the selected Windows player first.' }
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$evidenceFolder = Join-Path $projectRoot ('evidence\local\smoke-' + $stamp)
New-Item -ItemType Directory -Path $evidenceFolder | Out-Null
$runArguments = @('-citylifeSmoke', '-citylifeEvidence', ('"' + $evidenceFolder + '"'), '-logFile', ('"' + (Join-Path $evidenceFolder 'player.log') + '"'))
# The isolated mode renders synthetic scene views and disables all game input/cursor operations.
# This launcher neither attaches to nor changes an existing player or desktop window.
$runProcess = Start-Process -FilePath $player -ArgumentList $runArguments -WindowStyle Hidden -PassThru
if (-not $runProcess.WaitForExit(120000)) {
    # Stop only the exact child launched here, never another user-owned player.
    $runProcess.Kill()
    throw "Isolated route timed out; inspect $evidenceFolder"
}
if ($runProcess.ExitCode -ne 0) { throw "Isolated route failed ($($runProcess.ExitCode)); inspect $evidenceFolder" }
Write-Output "Isolated scene evidence: $evidenceFolder"
Write-Output 'Native controls, window presentation and UI acceptance require a separate permitted interactive check.'
