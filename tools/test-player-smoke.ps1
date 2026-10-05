param(
    [string]$Player = '',
    [int]$TimeoutSec = 300,
    [string]$npcSurvivalSave = '',
    [string]$physicalSave = '',
    [string]$EvidenceDir = ''
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

# Default player executable resolution: look for newest KookerStarfallIntegrated.exe in Builds
if (-not $Player) {
    $candidates = Get-ChildItem -Path (Join-Path $project 'Builds') -Recurse -Filter 'KookerStarfallIntegrated.exe' | Sort-Object LastWriteTime -Descending
    if ($candidates.Count -gt 0) {
        $playerPath = $candidates[0].FullName
    } else {
        $playerPath = (Resolve-Path -LiteralPath (Join-Path $project 'Builds/KookerStarfallIntegrated-0.0.11-survival.1-20260919-072218/KookerStarfallIntegrated.exe')).Path
    }
} else {
    $playerPath = (Resolve-Path -LiteralPath (Join-Path $project $Player)).Path
}

# Timestamped isolated run directory: NEVER delete shared evidence directory!
$timestamp = (Get-Date -Format 'yyyyMMdd-HHmmss')
if ($EvidenceDir) {
    $runDir = [System.IO.Path]::GetFullPath($EvidenceDir)
} else {
    $runDir = Join-Path $project "evidence/local/integrated-acceptance-test/runs/run-$timestamp"
}
$null = New-Item -ItemType Directory -Force -Path $runDir

# Separate directories for Process 1 and Process 2 to avoid evidence collisions and guard violations
$p1AcceptanceDir = Join-Path $runDir 'acceptance-p1'
$null = New-Item -ItemType Directory -Force -Path $p1AcceptanceDir
$p1SurvivalEvidenceDir = Join-Path $runDir 'survival-p1'
$null = New-Item -ItemType Directory -Force -Path $p1SurvivalEvidenceDir
$logFile1 = Join-Path $runDir 'player-p1.log'

$p2AcceptanceDir = Join-Path $runDir 'acceptance-p2'
$null = New-Item -ItemType Directory -Force -Path $p2AcceptanceDir
$p2SurvivalEvidenceDir = Join-Path $runDir 'survival-p2'
$null = New-Item -ItemType Directory -Force -Path $p2SurvivalEvidenceDir
$logFile2 = Join-Path $runDir 'player-p2.log'

# Isolated saves directory: use a compact path under c:\workstreams\smoke-saves to avoid Win32 MAX_PATH (260 char) overflow
$isolatedSavesRoot = "C:\workstreams\smoke-saves\run-$timestamp"
$null = New-Item -ItemType Directory -Force -Path $isolatedSavesRoot
$saveDir = Join-Path $runDir 'saves'
$null = New-Item -ItemType Directory -Force -Path $saveDir

$isolatedSurvival = if ($npcSurvivalSave) { [System.IO.Path]::GetFullPath($npcSurvivalSave) } else { Join-Path $isolatedSavesRoot 'starfall-survival-save.json' }
$isolatedPhysical = if ($physicalSave) { [System.IO.Path]::GetFullPath($physicalSave) } else { Join-Path $isolatedSavesRoot 'starfall-physical-save.json' }

# Verify default save pointer before launch: MUST REMAIN UNTOUCHED
$defaultSavePointer = 'C:\Users\irwin\AppData\LocalLow\LocalWorldStudy\Kooker Starfall - Integrated Coastal Candidate\starfall-survival-save.json'
$hashBefore = if (Test-Path -LiteralPath $defaultSavePointer) { (Get-FileHash -LiteralPath $defaultSavePointer -Algorithm SHA256).Hash } else { $null }
Write-Output "Default save pointer before launch: $hashBefore"

# =============================================================================
# PROCESS 1: INITIAL PLAYTHROUGH, FISHING FLOW, AND COORD CHECKPOINT COMMIT
# =============================================================================
Write-Output "--- LAUNCHING PROCESS 1: FRESH ISOLATED ACCEPTANCE RUN ---"
$args1 = @(
    '-batchmode',
    '-force-d3d11',
    '-integratedSmoke',
    '-integratedEvidence', ('"' + $p1AcceptanceDir + '"'),
    '-npcSurvivalEvidence', ('"' + $p1SurvivalEvidenceDir + '"'),
    '-npcSurvivalSave', ('"' + $isolatedSurvival + '"'),
    '-physicalSave', ('"' + $isolatedPhysical + '"'),
    '-logFile', ('"' + $logFile1 + '"')
)
Write-Output "Launching standalone player P1: $playerPath"
Write-Output "P1 Acceptance Evidence: $p1AcceptanceDir"
Write-Output "P1 Survival Evidence: $p1SurvivalEvidenceDir"
Write-Output "Isolated survival save: $isolatedSurvival"
Write-Output "Isolated physical save: $isolatedPhysical"

$proc1 = Start-Process -FilePath $playerPath -ArgumentList $args1 -PassThru
$watch1 = [Diagnostics.Stopwatch]::StartNew()
$finished1 = $proc1.WaitForExit($TimeoutSec * 1000)
$watch1.Stop()

if (-not $finished1) {
    try { $proc1.Kill() } catch {}
    throw "Player P1 timed out after $TimeoutSec seconds"
}

Write-Output "Player P1 exit code: $($proc1.ExitCode) in $([Math]::Round($watch1.Elapsed.TotalSeconds, 1))s"

# Verify default save pointer after P1 launch: MUST NOT HAVE CHANGED
$hashAfterP1 = if (Test-Path -LiteralPath $defaultSavePointer) { (Get-FileHash -LiteralPath $defaultSavePointer -Algorithm SHA256).Hash } else { $null }
Write-Output "Default save pointer after P1: $hashAfterP1"
if ($hashBefore -ne $hashAfterP1) {
    throw "CRITICAL DEFECT: Default save pointer was modified during Process 1! Expected $hashBefore, got $hashAfterP1."
} else {
    Write-Output "Default save pointer integrity verified: byte-identical before/after P1."
}

if ($proc1.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $logFile1) {
        Write-Output "player-p1.log tail:"
        Get-Content $logFile1 -Tail 40 | Out-Host
    }
    throw "Player P1 exited with nonzero code: $($proc1.ExitCode)"
}

$p1RuntimeJson = Join-Path $p1AcceptanceDir 'integrated-runtime.json'
if (-not (Test-Path -LiteralPath $p1RuntimeJson)) {
    if (Test-Path -LiteralPath $logFile1) {
        Write-Output "player-p1.log tail:"
        Get-Content $logFile1 -Tail 40 | Out-Host
    }
    throw "Runtime report missing for P1: $p1RuntimeJson"
}

$rawJson1 = Get-Content -LiteralPath $p1RuntimeJson -Raw
$reportObj1 = $rawJson1 | ConvertFrom-Json
Write-Output "P1 Report Status: $($reportObj1.status)"
if ($reportObj1.status -ne "PASS_AUTOMATED_NATIVE_AND_COVERAGE_REVIEW_PENDING" -and $reportObj1.status -ne "PASS") {
    throw "Integrated acceptance report status for P1 is NOT PASS: $($reportObj1.status)"
}

$p1ProofPath = Join-Path $p1AcceptanceDir 'fish-ownership-proof.json'
if (-not (Test-Path -LiteralPath $p1ProofPath)) {
    throw "CRITICAL: fish-ownership-proof.json not produced by Process 1 at: $p1ProofPath"
}
Write-Output "SMOKE_ACCEPTANCE_PROCESS_1_SUCCESS: Process 1 write verified cleanly."

# =============================================================================
# FRESH PROCESS 2: CROSS-PROCESS RELOAD AND EXACT OWNERSHIP VERIFICATION
# =============================================================================
Write-Output "--- STAGING AND LAUNCHING FRESH PROCESS 2 FOR RELOAD VERIFICATION ---"
# Copy proof record from P1 into P2 acceptance directory so P2 can verify reload
Copy-Item -LiteralPath $p1ProofPath -Destination (Join-Path $p2AcceptanceDir 'fish-ownership-proof.json') -Force
Write-Output "Staged fish-ownership-proof.json into Process 2 acceptance directory: $p2AcceptanceDir"

$args2 = @(
    '-batchmode',
    '-force-d3d11',
    '-integratedSmoke',
    '-integratedEvidence', ('"' + $p2AcceptanceDir + '"'),
    '-npcSurvivalEvidence', ('"' + $p2SurvivalEvidenceDir + '"'),
    '-npcSurvivalSave', ('"' + $isolatedSurvival + '"'),
    '-physicalSave', ('"' + $isolatedPhysical + '"'),
    '-logFile', ('"' + $logFile2 + '"')
)

$proc2 = Start-Process -FilePath $playerPath -ArgumentList $args2 -PassThru
$watch2 = [Diagnostics.Stopwatch]::StartNew()
$finished2 = $proc2.WaitForExit($TimeoutSec * 1000)
$watch2.Stop()

if (-not $finished2) {
    try { $proc2.Kill() } catch {}
    throw "Process 2 timed out after $TimeoutSec seconds"
}

Write-Output "Process 2 exit code: $($proc2.ExitCode) in $([Math]::Round($watch2.Elapsed.TotalSeconds, 1))s"

# Verify default save pointer after Process 2: MUST NOT HAVE CHANGED
$hashAfterP2 = if (Test-Path -LiteralPath $defaultSavePointer) { (Get-FileHash -LiteralPath $defaultSavePointer -Algorithm SHA256).Hash } else { $null }
Write-Output "Default save pointer after Process 2: $hashAfterP2"
if ($hashBefore -ne $hashAfterP2) {
    throw "CRITICAL DEFECT: Default save pointer was modified during Process 2! Expected $hashBefore, got $hashAfterP2."
} else {
    Write-Output "Default save pointer integrity verified: byte-identical before/after Process 2."
}

if ($proc2.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $logFile2) {
        Write-Output "player-p2.log tail:"
        Get-Content $logFile2 -Tail 40 | Out-Host
    }
    throw "Process 2 exited with nonzero code: $($proc2.ExitCode)"
}

$p2RuntimeJson = Join-Path $p2AcceptanceDir 'integrated-runtime.json'
if (-not (Test-Path -LiteralPath $p2RuntimeJson)) {
    if (Test-Path -LiteralPath $logFile2) {
        Write-Output "player-p2.log tail:"
        Get-Content $logFile2 -Tail 40 | Out-Host
    }
    throw "Process 2 runtime report missing: $p2RuntimeJson"
}

$rawJson2 = Get-Content -LiteralPath $p2RuntimeJson -Raw
$reportObj2 = $rawJson2 | ConvertFrom-Json
Write-Output "P2 Report Status: $($reportObj2.status)"
if ($reportObj2.status -ne "PASS_AUTOMATED_NATIVE_AND_COVERAGE_REVIEW_PENDING" -and $reportObj2.status -ne "PASS") {
    throw "Process 2 integrated acceptance report status is NOT PASS: $($reportObj2.status)"
}

# Archive isolated saves into run directory
try {
    Copy-Item -Path "$isolatedSavesRoot\*" -Destination $saveDir -Recurse -Force -ErrorAction SilentlyContinue
} catch {}

Write-Output "SMOKE_ACCEPTANCE_SUCCESS: Both Process 1 write and Process 2 reload verified cleanly with exact fish ownership."
