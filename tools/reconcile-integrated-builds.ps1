param(
    [Parameter(Mandatory)][string]$FallbackManifest,
    [string]$FallbackRuntime,
    [Parameter(Mandatory)][string]$CandidateManifest,
    [string]$CandidateRuntime,
    [switch]$TemporaryCandidate,
    [switch]$Execute,
    [scriptblock]$ProcessProvider = { Get-CimInstance Win32_Process }
)
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$builds=(Resolve-Path -LiteralPath (Join-Path $project 'Builds')).Path
$evidenceRoot=(Resolve-Path -LiteralPath (Join-Path $project 'evidence/milestones/coastal')).Path
if((Get-Item -LiteralPath $builds).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked build root rejected.'}
if(@(Get-Process Unity -ErrorAction SilentlyContinue).Count){throw 'Unity active; defer cleanup.'}
if(!$TemporaryCandidate -and !$CandidateRuntime){throw 'A tested candidate requires its runtime receipt.'}
$processes=& $ProcessProvider
$idPattern='^KookerStarfallIntegrated-0\.0\.[0-9]+-[a-z]+\.[0-9]+-[0-9]{8}-[0-9]{6}$'
function InspectKeep([string]$manifestPath,[string]$runtimePath,[bool]$temporary){
    $manifestFile=(Resolve-Path -LiteralPath $manifestPath).Path
    if([IO.Path]::GetFileName($manifestFile) -ne 'preview-build.json' -or
        ![IO.Path]::GetDirectoryName($manifestFile).StartsWith($evidenceRoot+'\',[StringComparison]::OrdinalIgnoreCase)){
        throw 'Keep manifest outside coastal evidence.'
    }
    $m=Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
    if($m.status -ne 'Succeeded' -or $m.buildId -notmatch $idPattern -or
        $m.output -ne ('Builds/'+$m.buildId+'/KookerStarfallIntegrated.exe')){throw 'Invalid keep manifest identity.'}
    $dir=Join-Path $builds $m.buildId
    if(!(Test-Path -LiteralPath $dir -PathType Container) -or
        (Get-Item -LiteralPath $dir).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Keep player absent or linked.'}
    if($temporary){
        if($runtimePath){throw 'Temporary candidate must not imply a verified runtime receipt.'}
        $files=@(Get-ChildItem -LiteralPath $dir -Recurse -File -Force)
        $bytes=($files | Measure-Object Length -Sum).Sum
        if(!$files.Count -or $bytes -ne $m.bytes){throw 'Temporary candidate file inventory disagrees with manifest.'}
        $content=& (Join-Path $PSScriptRoot 'get-integrated-content-hash.ps1') -BuildDirectory $dir
        [pscustomobject]@{id=$m.buildId;manifest=$manifestFile;runtime='UNVERIFIED_TEMPORARY';
            buildContentSha256=$content.sha256;source=$m.sourceCommit}
    }elseif(!$runtimePath){
        # Build-completeness proof without separate gameplay runtime receipt
        $files=@(Get-ChildItem -LiteralPath $dir -Recurse -File -Force)
        $bytes=($files | Measure-Object Length -Sum).Sum
        if(!$files.Count -or $bytes -ne $m.bytes){throw 'Fallback file inventory disagrees with manifest.'}
        $content=& (Join-Path $PSScriptRoot 'get-integrated-content-hash.ps1') -BuildDirectory $dir
        [pscustomobject]@{id=$m.buildId;manifest=$manifestFile;runtime='BUILD_COMPLETE_UNTESTED_RUNTIME';
            buildContentSha256=$content.sha256;source=$m.sourceCommit}
    }else{
        $preflight=(& (Join-Path $PSScriptRoot 'check-integrated-build.ps1') -BuildManifest $manifestFile -RuntimeDirectory $runtimePath | ConvertFrom-Json)
        if($preflight.status -ne 'IDENTITY_AND_AUTOMATED_RECEIPTS_MATCH_NOT_RELEASE_ACCEPTANCE' -or
            $preflight.build -ne $m.buildId){throw 'Keep runtime preflight mismatch.'}
        [pscustomobject]@{id=$m.buildId;manifest=$manifestFile;runtime=(Resolve-Path -LiteralPath $runtimePath).Path;
            buildContentSha256=$preflight.buildContentSha256;source=$m.sourceCommit}
    }
}
$fallback=InspectKeep $FallbackManifest $FallbackRuntime $false
$candidate=InspectKeep $CandidateManifest $CandidateRuntime ([bool]$TemporaryCandidate)
if($fallback.id -eq $candidate.id -or
    $fallback.id.Substring($fallback.id.Length-15) -gt $candidate.id.Substring($candidate.id.Length-15)){
    throw 'Choose distinct ordered fallback and current candidate.'
}
$known=@{}
Get-ChildItem -LiteralPath $evidenceRoot -Recurse -Filter preview-build.json -File | ForEach-Object {
    $m=Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    if($m.status -eq 'Succeeded' -and $m.buildId -match $idPattern -and
        $m.output -eq ('Builds/'+$m.buildId+'/KookerStarfallIntegrated.exe')){
        if($known.ContainsKey($m.buildId)){throw ('Duplicate source manifest for '+$m.buildId)}
        $known[$m.buildId]=$_.FullName
    }
}
$targets=@();$skipped=@()
foreach($dir in Get-ChildItem -LiteralPath $builds -Directory){
    if($dir.Name -eq $candidate.id -or $dir.Name -eq 'Download'){continue}
    if($dir.Name -notmatch $idPattern){
        $skipped += [pscustomobject]@{path=$dir.FullName;reason='UNRECOGNIZED_BUILD_NAME_PATTERN';bytes=$null}
        continue
    }
    if(!$known.ContainsKey($dir.Name)){
        $skipped += [pscustomobject]@{path=$dir.FullName;reason='UNMANIFESTED_BUILD_DIRECTORY';bytes=$null}
        continue
    }
    if($dir.Name.Substring($dir.Name.Length-15) -gt $candidate.id.Substring($candidate.id.Length-15)){
        $skipped += [pscustomobject]@{path=$dir.FullName;reason='NEWER_THAN_CANDIDATE';bytes=$null}
        continue
    }
    if([IO.Path]::GetDirectoryName($dir.FullName) -ne $builds){throw 'Target is not a direct build child.'}
    $activeProcesses=@($processes | Where-Object { $_.ExecutablePath -and
        $_.ExecutablePath.StartsWith($dir.FullName+'\',[StringComparison]::OrdinalIgnoreCase) })
    if($activeProcesses.Count -gt 0){
        $skipped += [pscustomobject]@{path=$dir.FullName;reason='ACTIVE_RUNNING_PROCESS';bytes=$null;pids=@($activeProcesses | ForEach-Object { $_.ProcessId })}
        continue
    }
    $items=@($dir)+@(Get-ChildItem -LiteralPath $dir.FullName -Recurse -Force)
    if(@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count){throw 'Linked target rejected.'}
    $protectedState=@($items | Where-Object { ($_.PSIsContainer -and $_.Name -match '^(?i:saves?|memory|ledger|private)$') -or
        $_.Name -match '(?i)\.(sqlite|sqlite3|db|jsonl)$' })
    if($protectedState.Count -gt 0){
        $skipped += [pscustomobject]@{path=$dir.FullName;reason='PROTECTED_USER_STATE_OR_DATABASE';bytes=$null;matched=@($protectedState | ForEach-Object { $_.Name })}
        continue
    }
    $content=& (Join-Path $PSScriptRoot 'get-integrated-content-hash.ps1') -BuildDirectory $dir.FullName
    $targets += [pscustomobject]@{path=$dir.FullName;manifest=$known[$dir.Name];
        buildContentSha256=$content.sha256;files=$content.files;
        bytes=($items | Where-Object {!$_.PSIsContainer} | Measure-Object Length -Sum).Sum}
}
$receipt=[ordered]@{
    schema='starfall.build-reconciliation.v1';status='DRY_RUN_NOT_RELEASE';
    fallback=$fallback;candidate=$candidate;candidateTemporary=[bool]$TemporaryCandidate;
    targets=$targets;skipped=$skipped;
    targetCount=@($targets).Count;targetBytes=($targets | Measure-Object bytes -Sum).Sum;
    deletedTargets=@();deletedBytes=0;remainingBuildCount=$null;remainingBuildPaths=@();
    freeBefore=(Get-PSDrive C).Free;freeAfter=$null;auditPath=$null;
    boundary='Only manifest-known superseded direct player build directories; single successful replacement retained, active builds deferred, source, saves and evidence remain.'
}
if(!$Execute){return [pscustomobject]$receipt}
$audit=Join-Path $project ('evidence/local/build-retention/reconcile-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$null=New-Item -ItemType Directory -Path $audit -Force
$auditFile=Join-Path $audit 'reconciliation.json'
$receipt.auditPath=$auditFile
$receipt.status='REMOVAL_STARTED';$receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $auditFile
$deletedTargets=@()
$deletedBytes=0
try{
    foreach($target in $targets){
        if([IO.Path]::GetDirectoryName($target.path) -ne $builds -or
            $target.path -eq (Join-Path $builds $candidate.id)){throw 'Target changed during cleanup.'}

        # Immediate pre-delete active process recheck
        $liveProcesses=& $ProcessProvider
        $targetActive=@($liveProcesses | Where-Object { $_.ExecutablePath -and
            $_.ExecutablePath.StartsWith($target.path+'\',[StringComparison]::OrdinalIgnoreCase) })
        if($targetActive.Count -gt 0){
            $deferredItem=[pscustomobject]@{path=$target.path;reason='ACTIVE_RUNNING_PROCESS_STARTED_DURING_CLEANUP';bytes=$target.bytes;pids=@($targetActive | ForEach-Object { $_.ProcessId })}
            $skipped += $deferredItem
            continue
        }

        Remove-Item -LiteralPath $target.path -Recurse -Force
        if(Test-Path -LiteralPath $target.path){throw ('Target remained: '+$target.path)}

        $deletedTargets += [pscustomobject]@{path=$target.path;bytes=$target.bytes}
        $deletedBytes += $target.bytes
        $receipt.deletedTargets=$deletedTargets
        $receipt.deletedBytes=$deletedBytes
        $receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $auditFile
    }

    $receipt.skipped=$skipped
    $remainingBuildDirs=@(Get-ChildItem -LiteralPath $builds -Directory | Where-Object { $_.Name -match $idPattern })
    $remainingCount=$remainingBuildDirs.Count
    $receipt.remainingBuildCount=$remainingCount
    $receipt.remainingBuildPaths=@($remainingBuildDirs | ForEach-Object { $_.FullName })

    # Re-verify candidate integrity
    $null=InspectKeep $CandidateManifest $CandidateRuntime ([bool]$TemporaryCandidate)

    # Determine status truthfully based on remaining count and actual skip reasons
    if($remainingCount -eq 1 -and $remainingBuildDirs[0].Name -eq $candidate.id){
        $receipt.status='RETAINED_SINGLE_REPLACEMENT_BUILD_NOT_RELEASE'
    }else{
        $activeSkips=@($skipped | Where-Object { $_.reason -like 'ACTIVE_*' })
        $protectedSkips=@($skipped | Where-Object { $_.reason -like 'PROTECTED_*' })
        if($activeSkips.Count -gt 0){
            $receipt.status='RETAINED_REPLACEMENT_AND_ACTIVE_BUILDS_DEFERRED_NOT_RELEASE'
        }elseif($protectedSkips.Count -gt 0){
            $receipt.status='RETAINED_REPLACEMENT_AND_PROTECTED_STATE_BUILDS_NOT_RELEASE'
        }else{
            $receipt.status='RETAINED_MULTIPLE_BUILDS_NOT_RELEASE'
        }
    }

    $receipt.freeAfter=(Get-PSDrive C).Free
    $receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $auditFile
}catch{
    $receipt.status='PARTIAL_OR_FAILED_REVIEW_REQUIRED'
    $receipt.deletedTargets=$deletedTargets
    $receipt.deletedBytes=$deletedBytes
    $receipt.remainingBuildCount=@(Get-ChildItem -LiteralPath $builds -Directory | Where-Object { $_.Name -match $idPattern }).Count
    $receipt.error=$_.Exception.Message
    $receipt.freeAfter=(Get-PSDrive C).Free
    $receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $auditFile
    throw
}
[pscustomobject]$receipt
