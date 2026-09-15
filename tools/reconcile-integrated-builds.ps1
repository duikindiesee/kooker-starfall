param(
    [Parameter(Mandatory)][string]$FallbackManifest,
    [Parameter(Mandatory)][string]$FallbackRuntime,
    [Parameter(Mandatory)][string]$CandidateManifest,
    [string]$CandidateRuntime,
    [switch]$TemporaryCandidate,
    [switch]$Execute
)
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$builds=(Resolve-Path -LiteralPath (Join-Path $project 'Builds')).Path
$evidenceRoot=(Resolve-Path -LiteralPath (Join-Path $project 'evidence/milestones/coastal')).Path
if((Get-Item -LiteralPath $builds).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked build root rejected.'}
if(@(Get-Process Unity,KookerStarfallIntegrated -ErrorAction SilentlyContinue).Count){throw 'Unity/player active; defer cleanup.'}
if(!$TemporaryCandidate -and !$CandidateRuntime){throw 'A tested candidate requires its runtime receipt.'}
$processes=Get-CimInstance Win32_Process
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
    if($dir.Name -eq $fallback.id -or $dir.Name -eq $candidate.id -or $dir.Name -eq 'Download'){continue}
    if($dir.Name -notmatch $idPattern -or !$known.ContainsKey($dir.Name) -or
        $dir.Name.Substring($dir.Name.Length-15) -gt $candidate.id.Substring($candidate.id.Length-15)){
        $skipped+=$dir.FullName;continue
    }
    if([IO.Path]::GetDirectoryName($dir.FullName) -ne $builds){throw 'Target is not a direct build child.'}
    if(@($processes | Where-Object { $_.ExecutablePath -and
        $_.ExecutablePath.StartsWith($dir.FullName+'\',[StringComparison]::OrdinalIgnoreCase) }).Count){throw 'Target build is active.'}
    $items=@($dir)+@(Get-ChildItem -LiteralPath $dir.FullName -Recurse -Force)
    if(@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count){throw 'Linked target rejected.'}
    if(@($items | Where-Object { ($_.PSIsContainer -and $_.Name -match '^(?i:saves?|memory|ledger|private)$') -or
        $_.Name -match '(?i)\.(sqlite|sqlite3|db|jsonl)$' }).Count){$skipped+=$dir.FullName;continue}
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
    freeBefore=(Get-PSDrive C).Free;freeAfter=$null;auditPath=$null;
    boundary='Only manifest-known superseded direct player build directories; source, saves, evidence and unaccepted current candidate remain.'
}
if(!$Execute){return [pscustomobject]$receipt}
$audit=Join-Path $project ('evidence/local/build-retention/reconcile-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$null=New-Item -ItemType Directory -Path $audit
$auditFile=Join-Path $audit 'reconciliation.json'
$receipt.auditPath=$auditFile
$receipt.status='REMOVAL_STARTED';$receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $auditFile
try{
    foreach($target in $targets){
        if([IO.Path]::GetDirectoryName($target.path) -ne $builds -or
            $target.path -eq (Join-Path $builds $fallback.id) -or
            $target.path -eq (Join-Path $builds $candidate.id)){throw 'Target changed during cleanup.'}
        Remove-Item -LiteralPath $target.path -Recurse -Force
        if(Test-Path -LiteralPath $target.path){throw ('Target remained: '+$target.path)}
    }
    $null=InspectKeep $FallbackManifest $FallbackRuntime $false
    $null=InspectKeep $CandidateManifest $CandidateRuntime ([bool]$TemporaryCandidate)
    if($TemporaryCandidate){$receipt.status='RETAINED_VERIFIED_FALLBACK_AND_TEMPORARY_CANDIDATE_NOT_RELEASE'}
    else{$receipt.status='RETAINED_FALLBACK_AND_TESTED_CANDIDATE_NOT_RELEASE'}
    $receipt.freeAfter=(Get-PSDrive C).Free
    $receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $auditFile
}catch{
    $receipt.status='PARTIAL_OR_FAILED_REVIEW_REQUIRED'
    $receipt.freeAfter=(Get-PSDrive C).Free
    $receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $auditFile
    throw
}
[pscustomobject]$receipt
