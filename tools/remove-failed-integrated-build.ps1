param([Parameter(Mandatory=$true)][string]$Manifest,[string]$FailedRuntimeDirectory,[switch]$Execute)
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$buildRoot=(Resolve-Path -LiteralPath (Join-Path $project 'Builds')).Path
$receiptSource=(Resolve-Path -LiteralPath $Manifest).Path
$evidenceRoot=Join-Path $project 'evidence/milestones/coastal'
if (!$receiptSource.StartsWith($evidenceRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest must be coastal milestone evidence.' }
$m=Get-Content -LiteralPath $receiptSource -Raw | ConvertFrom-Json
if ($FailedRuntimeDirectory) {
    $runtimeRoot=(Resolve-Path -LiteralPath $FailedRuntimeDirectory).Path
    if(!$runtimeRoot.StartsWith((Join-Path $project 'evidence/local')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Runtime evidence outside local evidence root.'}
    $launch=Get-Content -LiteralPath (Join-Path $runtimeRoot 'launch.json') -Raw|ConvertFrom-Json
    $runtime=Get-Content -LiteralPath (Join-Path $runtimeRoot 'runtime/integrated-runtime.json') -Raw|ConvertFrom-Json
    if($runtime.status -ne 'FAIL' -or $launch.build -ne $m.buildId -or $launch.source_commit -ne $m.sourceCommit){throw 'Require exact failed runtime identity.'}
    foreach($prior in Get-ChildItem -LiteralPath (Join-Path $project 'evidence/local') -Recurse -Filter launch.json){
        $identity=Get-Content -LiteralPath $prior.FullName -Raw|ConvertFrom-Json
        $priorReport=Join-Path $prior.DirectoryName 'runtime/integrated-runtime.json'
        if($identity.build -eq $m.buildId -and (Test-Path -LiteralPath $priorReport)){
            $p=Get-Content -LiteralPath $priorReport -Raw|ConvertFrom-Json
            if($p.status -eq 'PASS_AUTOMATED_NATIVE_AND_COVERAGE_REVIEW_PENDING'){throw 'Previously verified player protected from runtime-failure cleanup.'}
        }
    }
} elseif ($m.status -ne 'Failed') { throw 'Only explicit Failed build receipts authorize cleanup.' }
if ($m.buildId -notmatch '^KookerStarfallIntegrated-0\.0\.[0-9]+-[a-z]+\.[0-9]+-[0-9]{8}-[0-9]{6}$') { throw 'Unexpected build identity.' }
$expected=Join-Path (Join-Path $buildRoot $m.buildId) 'KookerStarfallIntegrated.exe'
$output=[IO.Path]::GetFullPath((Join-Path $project $m.output))
if ($output -ne $expected) { throw 'Output does not match the exact build identity.' }
$target=[IO.Path]::GetDirectoryName($output)
if ([IO.Path]::GetDirectoryName($target) -ne $buildRoot) { throw 'Target is not a direct build-output child.' }
foreach($other in Get-ChildItem -LiteralPath $evidenceRoot -Recurse -Filter preview-build.json) {
    $record=Get-Content -LiteralPath $other.FullName -Raw | ConvertFrom-Json
    if (!$FailedRuntimeDirectory -and $record.buildId -eq $m.buildId -and $record.status -eq 'Succeeded') { throw 'A successful receipt protects this output.' }
}
if (!(Test-Path -LiteralPath $target)) { return [pscustomobject]@{status='ALREADY_ABSENT';target=$target} }
if (@(Get-Process Unity -ErrorAction SilentlyContinue).Count) { throw 'Unity is active; defer cleanup.' }
$processes=Get-CimInstance Win32_Process
if (@($processes | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase) }).Count) { throw 'A process is using this build.' }
$items=@(Get-Item -LiteralPath $buildRoot)+@(Get-Item -LiteralPath $target)+@(Get-ChildItem -LiteralPath $target -Recurse -Force)
if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Reparse points are not eligible for cleanup.' }
if (@($items | Where-Object { ($_.PSIsContainer -and $_.Name -match '^(?i:saves?|memory|ledger)$') -or $_.Name -match '(?i)\.(sqlite|sqlite3|db|jsonl)$' }).Count) { throw 'Possible user state detected; manual review required.' }
$files=@($items | Where-Object { !$_.PSIsContainer })
$bytes=($files | Measure-Object Length -Sum).Sum
$result=[ordered]@{status='DRY_RUN';target=$target;manifest=$receiptSource;buildId=$m.buildId;sourceCommit=$m.sourceCommit;bytes=$bytes;fileCount=$files.Count;freeBefore=(Get-PSDrive C).Free;utc=[DateTime]::UtcNow.ToString('o')}
if (!$Execute) { return [pscustomobject]$result }
$audit=Join-Path $project ('evidence/local/failed-build-cleanup/'+$m.buildId+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$null=New-Item -ItemType Directory -Path $audit
Copy-Item -LiteralPath $receiptSource -Destination (Join-Path $audit 'failed-build.json')
$files | Select-Object @{n='relativePath';e={$_.FullName.Substring($target.Length+1)}},Length | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $audit 'file-inventory.json')
$result.status='REMOVAL_STARTED'
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $audit 'cleanup.json')
# Exact validated target only; source, generated assets, saves and external logs stay intact.
Remove-Item -LiteralPath $target -Recurse -Force
if (Test-Path -LiteralPath $target) { throw 'Build output remains after cleanup.' }
$result.status='REMOVED_FAILED_BUILD'
$result.freeAfter=(Get-PSDrive C).Free
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $audit 'cleanup.json')
[pscustomobject]$result
