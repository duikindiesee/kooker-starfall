param([Parameter(Mandatory)][string]$BuildManifest,[Parameter(Mandatory)][string]$RuntimeDirectory,[switch]$Execute)
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'check-integrated-build.ps1') -BuildManifest $BuildManifest -RuntimeDirectory $RuntimeDirectory | Out-Host
$keep=Get-Content -LiteralPath $BuildManifest -Raw | ConvertFrom-Json
$builds=(Resolve-Path -LiteralPath (Join-Path $project 'Builds')).Path
if ((Get-Item -LiteralPath $builds).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked build root rejected.' }
if (@(Get-Process Unity -ErrorAction SilentlyContinue).Count) { throw 'Unity active; defer retention.' }
$processes=Get-CimInstance Win32_Process
$known=@{}
Get-ChildItem -LiteralPath (Join-Path $project 'evidence/milestones/coastal') -Recurse -Filter preview-build.json | ForEach-Object {
    $m=Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    if($m.buildId -and $m.output -eq ('Builds/'+$m.buildId+'/KookerStarfallIntegrated.exe')) { $known[$m.buildId]=$_.FullName }
}
$targets=@();$skipped=@()
foreach($dir in Get-ChildItem -LiteralPath $builds -Directory) {
    if($dir.Name -eq $keep.buildId -or $dir.Name -eq 'Download') { continue }
    if($dir.Name -notmatch '^KookerStarfallIntegrated-0\.0\.[0-9]+-[a-z]+\.[0-9]+-[0-9]{8}-[0-9]{6}$' -or !$known.ContainsKey($dir.Name)) { $skipped+=$dir.FullName;continue }
    # A newer temporary candidate is not superseded by promoting an older receipt.
    if($dir.Name.Substring($dir.Name.Length-15) -gt $keep.buildId.Substring($keep.buildId.Length-15)) { $skipped+=$dir.FullName;continue }
    if([IO.Path]::GetDirectoryName($dir.FullName) -ne $builds) { throw 'Outside direct build directory.' }
    if(@($processes|Where-Object {$_.ExecutablePath -and $_.ExecutablePath.StartsWith($dir.FullName+'\',[StringComparison]::OrdinalIgnoreCase)}).Count){throw 'Old build is active; defer cleanup.'}
    $items=@($dir)+@(Get-ChildItem -LiteralPath $dir.FullName -Recurse -Force)
    if(@($items|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Linked output rejected.'}
    if(@($items|Where-Object {($_.PSIsContainer -and $_.Name -match '^(?i:saves?|memory|ledger)$') -or $_.Name -match '(?i)\.(sqlite|sqlite3|db|jsonl)$'}).Count){$skipped+=$dir.FullName;continue}
    $targets += [pscustomobject]@{path=$dir.FullName;bytes=($items|Where-Object{!$_.PSIsContainer}|Measure-Object Length -Sum).Sum;manifest=$known[$dir.Name]}
}
$download=Join-Path $builds 'Download'
if(Test-Path -LiteralPath $download){
    if((Get-Item -LiteralPath $download).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked download directory rejected.'}
    foreach($zip in Get-ChildItem -LiteralPath $download -Filter '*-Windows.zip' -File){
        $id=$zip.Name -replace '-Windows.zip$',''
        if($id -eq $keep.buildId -or !$known.ContainsKey($id)){continue}
        if($id.Substring($id.Length-15) -gt $keep.buildId.Substring($keep.buildId.Length-15)){continue}
        if($zip.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked ZIP rejected.'}
        $targets += [pscustomobject]@{path=$zip.FullName;bytes=$zip.Length;manifest=$known[$id]}
    }
}
$result=[ordered]@{status='DRY_RUN';keep=$keep.buildId;bytes=($targets|Measure-Object bytes -Sum).Sum;targets=$targets;skipped=$skipped;freeBefore=(Get-PSDrive C).Free}
if(!$Execute){return [pscustomobject]$result}
$audit=Join-Path $project ('evidence/local/build-retention/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$null=New-Item -ItemType Directory -Path $audit
$result.status='REMOVAL_STARTED'
$result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $audit 'retention.json')
foreach($target in $targets){
    # Targets are validated direct build children or exact ZIPs; never source/evidence.
    Remove-Item -LiteralPath $target.path -Recurse -Force
    if(Test-Path -LiteralPath $target.path){throw 'Output remained after removal.'}
}
& (Join-Path $PSScriptRoot 'check-integrated-build.ps1') -BuildManifest $BuildManifest -RuntimeDirectory $RuntimeDirectory | Out-Host
$result.status='RETAINED_ONE_VERIFIED_BUILD'
$result.freeAfter=(Get-PSDrive C).Free
$result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $audit 'retention.json')
[pscustomobject]$result
