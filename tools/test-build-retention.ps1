# Complete behavioral and regression fixture test suite for Starfall One-Build Retention policy.
# Executes the actual production reconcile-integrated-builds.ps1 on an isolated copied fixture project.
# Strictly validates resolved temporary directory roots and reparse points; NO production Builds are touched.
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path
$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$testTmp = Join-Path $tempBase ('starfall-retention-fixture-' + [System.Guid]::NewGuid().ToString('N'))

Write-Host "================================================================================"
Write-Host "STARFALL ONE-BUILD RETENTION BEHAVIORAL FIXTURE TEST SUITE"
Write-Host "Timestamp: $([DateTime]::UtcNow.ToString('o'))"
Write-Host "Isolated Fixture: $testTmp"
Write-Host "================================================================================`n"

$syntaxPassed = 0
$behavioralPassed = 0
$failed = 0

function Assert-Syntax([bool]$condition, [string]$name, [string]$detail = '') {
    if ($condition) {
        $script:syntaxPassed++
        Write-Host "  [PASS_SYNTAX_ONLY] $name"
    } else {
        $script:failed++
        Write-Host "  [FAIL_SYNTAX] $name - $detail" -ForegroundColor Red
    }
}

function Assert-Behavior([bool]$condition, [string]$name, [string]$detail = '') {
    if ($condition) {
        $script:behavioralPassed++
        Write-Host "  [PASS_BEHAVIORAL] $name"
    } else {
        $script:failed++
        Write-Host "  [FAIL_BEHAVIORAL] $name - $detail" -ForegroundColor Red
    }
}

try {
    # Safety guard: ensure testTmp resolves strictly inside GetTempPath and is not a reparse point
    $null = New-Item -ItemType Directory -Path $testTmp -Force
    $resolvedTmp = (Resolve-Path -LiteralPath $testTmp).Path
    if (!$resolvedTmp.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Security violation: fixture root outside temp directory: $resolvedTmp"
    }
    if ((Get-Item -LiteralPath $resolvedTmp).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Security violation: fixture root is a reparse point: $resolvedTmp"
    }

    Write-Host "--- 1. Static Setup & Syntax Checks (Setup / Parameter Contracts Only) ---"
    $prodReconcile = Join-Path $scriptRoot 'reconcile-integrated-builds.ps1'
    $prodBuild = Join-Path $scriptRoot 'build-integrated.ps1'
    $prodHash = Join-Path $scriptRoot 'get-integrated-content-hash.ps1'
    $prodCheck = Join-Path $scriptRoot 'check-integrated-build.ps1'

    $tokens = $null; $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($prodReconcile, [ref]$tokens, [ref]$errors)
    Assert-Syntax ($errors.Count -eq 0) "reconcile-integrated-builds.ps1 parses without syntax errors"

    [System.Management.Automation.Language.Parser]::ParseFile($prodBuild, [ref]$tokens, [ref]$errors)
    Assert-Syntax ($errors.Count -eq 0) "build-integrated.ps1 parses without syntax errors"

    $cmd = Get-Command $prodReconcile
    $fbRuntimeParam = $cmd.Parameters['FallbackRuntime']
    Assert-Syntax ($fbRuntimeParam -ne $null -and $fbRuntimeParam.Attributes.Mandatory -ne $true) "FallbackRuntime parameter is non-mandatory (build-completeness fallback)"

    $candManifestParam = $cmd.Parameters['CandidateManifest']
    Assert-Syntax ($candManifestParam.Attributes.Mandatory -eq $true) "CandidateManifest parameter is mandatory"

    # Set up isolated copied fixture project
    Write-Host "`n--- 2. Setting Up Copied Fixture Project ---"
    $fixtureTools = Join-Path $testTmp 'tools'
    $fixtureBuilds = Join-Path $testTmp 'Builds'
    $fixtureEvidence = Join-Path $testTmp 'evidence/milestones/coastal'
    $fixtureLocal = Join-Path $testTmp 'evidence/local'
    $null = New-Item -ItemType Directory -Path $fixtureTools -Force
    $null = New-Item -ItemType Directory -Path $fixtureBuilds -Force
    $null = New-Item -ItemType Directory -Path $fixtureEvidence -Force
    $null = New-Item -ItemType Directory -Path $fixtureLocal -Force

    Copy-Item -LiteralPath $prodReconcile -Destination (Join-Path $fixtureTools 'reconcile-integrated-builds.ps1')
    Copy-Item -LiteralPath $prodHash -Destination (Join-Path $fixtureTools 'get-integrated-content-hash.ps1')
    Copy-Item -LiteralPath $prodCheck -Destination (Join-Path $fixtureTools 'check-integrated-build.ps1')

    $scriptToExecute = Join-Path $fixtureTools 'reconcile-integrated-builds.ps1'

    function New-FixtureBuild([string]$round, [string]$timestamp, [long]$fileBytes = 1024, [string]$extraFile = $null) {
        $buildId = "KookerStarfallIntegrated-0.0.11-coastal.1-$timestamp"
        $dir = Join-Path $fixtureBuilds $buildId
        $null = New-Item -ItemType Directory -Path $dir -Force
        $exe = Join-Path $dir 'KookerStarfallIntegrated.exe'
        [IO.File]::WriteAllBytes($exe, (New-Object byte[] $fileBytes))

        if ($extraFile) {
            $extraPath = Join-Path $dir $extraFile
            $extraDir = Split-Path $extraPath -Parent
            if (!(Test-Path -LiteralPath $extraDir)) { $null = New-Item -ItemType Directory -Path $extraDir -Force }
            [IO.File]::WriteAllBytes($extraPath, (New-Object byte[] 64))
        }

        $files = @(Get-ChildItem -LiteralPath $dir -Recurse -File -Force)
        $totalBytes = ($files | Measure-Object Length -Sum).Sum

        $roundDir = Join-Path $fixtureEvidence $round
        $null = New-Item -ItemType Directory -Path $roundDir -Force
        $manifestPath = Join-Path $roundDir 'preview-build.json'
        $manifest = [ordered]@{
            schema = 'starfall.build-preview.v1'
            status = 'Succeeded'
            buildId = $buildId
            sourceCommit = '019b587db04c6d60d20905c62e3e4b4db87d0196'
            output = "Builds/$buildId/KookerStarfallIntegrated.exe"
            bytes = $totalBytes
            version = '0.0.11-survival.1'
        }
        $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
        return @{ buildId = $buildId; dir = $dir; manifest = $manifestPath; bytes = $totalBytes }
    }

    Write-Host "`n--- 3. Executing Production Script: Scenario A (Dry-Run Non-Mutation) ---"
    $b0 = New-FixtureBuild -round 'round-101' -timestamp '20261001-100000' -fileBytes 2048
    $b1 = New-FixtureBuild -round 'round-102' -timestamp '20261002-120000' -fileBytes 4096

    $dryRunResult = & $scriptToExecute `
        -FallbackManifest $b0.manifest `
        -CandidateManifest $b1.manifest `
        -TemporaryCandidate `
        -ProcessProvider { @() }

    Assert-Behavior ($dryRunResult.status -eq 'DRY_RUN_NOT_RELEASE') "Dry run returned DRY_RUN_NOT_RELEASE status"
    Assert-Behavior ($dryRunResult.targetCount -eq 1 -and $dryRunResult.targets[0].path -eq $b0.dir) "Dry run identified b0 as superseded target"
    Assert-Behavior (Test-Path -LiteralPath $b0.dir) "Dry run non-mutation: superseded b0 was NOT deleted"
    Assert-Behavior (Test-Path -LiteralPath $b1.dir) "Dry run non-mutation: candidate b1 was NOT deleted"

    Write-Host "`n--- 4. Executing Production Script: Scenario B (One-Build Replacement Deletion) ---"
    $execResult = & $scriptToExecute `
        -FallbackManifest $b0.manifest `
        -CandidateManifest $b1.manifest `
        -TemporaryCandidate `
        -Execute `
        -ProcessProvider { @() }

    Assert-Behavior ($execResult.status -eq 'RETAINED_SINGLE_REPLACEMENT_BUILD_NOT_RELEASE') "Execution returned RETAINED_SINGLE_REPLACEMENT_BUILD_NOT_RELEASE status"
    Assert-Behavior (!(Test-Path -LiteralPath $b0.dir)) "Superseded old build b0 was deleted from disk"
    Assert-Behavior (Test-Path -LiteralPath $b1.dir) "Replacement candidate b1 is preserved on disk"
    Assert-Behavior ($execResult.remainingBuildCount -eq 1) "Truthful remainingBuildCount is exactly 1"
    Assert-Behavior (Test-Path -LiteralPath $execResult.auditPath) "Reconciliation audit receipt file exists"
    $auditReceipt = Get-Content -LiteralPath $execResult.auditPath -Raw | ConvertFrom-Json
    Assert-Behavior ($auditReceipt.deletedTargets.Count -eq 1 -and $auditReceipt.deletedBytes -eq $b0.bytes) "Incremental audit receipt records exact deleted target and bytes"

    Write-Host "`n--- 5. Executing Production Script: Scenario C (Active Process Deferral) ---"
    # Create bActive and bCandidate
    $bActive = New-FixtureBuild -round 'round-103' -timestamp '20261003-080000' -fileBytes 1024
    $bNewCandidate = New-FixtureBuild -round 'round-104' -timestamp '20261004-120000' -fileBytes 2048

    # Mock process observation returning an active process inside bActive directory
    $mockProc = [pscustomobject]@{
        ExecutablePath = (Join-Path $bActive.dir 'KookerStarfallIntegrated.exe')
        ProcessId = 99881
    }

    $activeDeferResult = & $scriptToExecute `
        -FallbackManifest $bActive.manifest `
        -CandidateManifest $bNewCandidate.manifest `
        -TemporaryCandidate `
        -Execute `
        -ProcessProvider { @($mockProc) }

    Assert-Behavior ($activeDeferResult.status -eq 'RETAINED_REPLACEMENT_AND_ACTIVE_BUILDS_DEFERRED_NOT_RELEASE') "Execution returned RETAINED_REPLACEMENT_AND_ACTIVE_BUILDS_DEFERRED_NOT_RELEASE"
    Assert-Behavior (Test-Path -LiteralPath $bActive.dir) "Actively running build bActive was deferred and NOT deleted"
    $activeSkip = @($activeDeferResult.skipped | Where-Object { $_.path -eq $bActive.dir })
    Assert-Behavior ($activeSkip.Count -eq 1 -and $activeSkip[0].reason -eq 'ACTIVE_RUNNING_PROCESS') "Structured skip reason records ACTIVE_RUNNING_PROCESS with PID"

    Write-Host "`n--- 6. Executing Production Script: Scenario D (Protected User State Retention) ---"
    $bProtected = New-FixtureBuild -round 'round-105' -timestamp '20261004-140000' -fileBytes 1024 -extraFile 'saves/inhabitant-state.json'
    $bFinalCandidate = New-FixtureBuild -round 'round-106' -timestamp '20261004-160000' -fileBytes 2048

    $protectedResult = & $scriptToExecute `
        -FallbackManifest $bProtected.manifest `
        -CandidateManifest $bFinalCandidate.manifest `
        -TemporaryCandidate `
        -Execute `
        -ProcessProvider { @() }

    Assert-Behavior ($protectedResult.status -eq 'RETAINED_REPLACEMENT_AND_PROTECTED_STATE_BUILDS_NOT_RELEASE') "Status records PROTECTED_STATE_BUILDS_NOT_RELEASE"
    Assert-Behavior (Test-Path -LiteralPath $bProtected.dir) "Build containing saves was NOT deleted"
    $protectSkip = @($protectedResult.skipped | Where-Object { $_.path -eq $bProtected.dir })
    Assert-Behavior ($protectSkip.Count -eq 1 -and $protectSkip[0].reason -eq 'PROTECTED_USER_STATE_OR_DATABASE') "Structured skip reason records PROTECTED_USER_STATE_OR_DATABASE"

    Write-Host "`n--- 7. Executing Production Script: Scenario E (Immediate Pre-Delete Active Recheck) ---"
    $bTargetPreDelete = New-FixtureBuild -round 'round-107' -timestamp '20261004-180000' -fileBytes 1024
    $bCandidatePreDelete = New-FixtureBuild -round 'round-108' -timestamp '20261004-200000' -fileBytes 2048

    # ProcessProvider returns empty during initial scanning, but returns active process on second invocation (pre-delete)
    $callCount = 0
    $dynamicProcessProvider = {
        $script:callCount++
        if ($script:callCount -gt 1) {
            return @([pscustomobject]@{
                ExecutablePath = (Join-Path $bTargetPreDelete.dir 'KookerStarfallIntegrated.exe')
                ProcessId = 77112
            })
        }
        return @()
    }

    $preDeleteResult = & $scriptToExecute `
        -FallbackManifest $bTargetPreDelete.manifest `
        -CandidateManifest $bCandidatePreDelete.manifest `
        -TemporaryCandidate `
        -Execute `
        -ProcessProvider $dynamicProcessProvider

    Assert-Behavior (Test-Path -LiteralPath $bTargetPreDelete.dir) "Target starting process mid-cleanup was caught by immediate pre-delete check and NOT deleted"
    $dynamicSkip = @($preDeleteResult.skipped | Where-Object { $_.reason -eq 'ACTIVE_RUNNING_PROCESS_STARTED_DURING_CLEANUP' })
    Assert-Behavior ($dynamicSkip.Count -ge 1) "Skip reason records ACTIVE_RUNNING_PROCESS_STARTED_DURING_CLEANUP"

    Write-Host "`n--- 8. Executing Production Script: Scenario F (Subsequent Build with FallbackRuntime Omitted) ---"
    # Clean fixture builds directory before Scenario F so it tests isolated single replacement
    Get-ChildItem -LiteralPath $fixtureBuilds -Directory | Remove-Item -Recurse -Force
    $bStep1 = New-FixtureBuild -round 'round-109' -timestamp '20261004-210000' -fileBytes 2048
    $bStep2 = New-FixtureBuild -round 'round-110' -timestamp '20261004-220000' -fileBytes 4096

    $secondBuildResult = & $scriptToExecute `
        -FallbackManifest $bStep1.manifest `
        -CandidateManifest $bStep2.manifest `
        -TemporaryCandidate `
        -Execute `
        -ProcessProvider { @() }

    Assert-Behavior ($secondBuildResult.status -eq 'RETAINED_SINGLE_REPLACEMENT_BUILD_NOT_RELEASE') "Subsequent build with omitted FallbackRuntime succeeds with build-completeness proof"
    Assert-Behavior (!(Test-Path -LiteralPath $bStep1.dir)) "Prior build bStep1 successfully superseded and deleted"
    Assert-Behavior (Test-Path -LiteralPath $bStep2.dir) "New replacement build bStep2 retained"

} finally {
    # Safe cleanup: verify root path strictly matches tempBase before removal
    if (Test-Path -LiteralPath $testTmp) {
        $resolvedFinal = (Resolve-Path -LiteralPath $testTmp).Path
        if ($resolvedFinal.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and
            !((Get-Item -LiteralPath $resolvedFinal).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Remove-Item -LiteralPath $resolvedFinal -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host "`n================================================================================"
Write-Host "FIXTURE TEST SUMMARY:"
Write-Host "  Setup & Syntax Assertions:       $syntaxPassed passed (Static / Parameter Checks Only)"
Write-Host "  Actual Behavioral Assertions:    $behavioralPassed passed (Production Script Executed on Fixture)"
Write-Host "  Failed:                          $failed failed"
Write-Host "================================================================================"

if ($failed -gt 0) {
    exit 1
}
