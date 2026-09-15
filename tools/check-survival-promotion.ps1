param([Parameter(Mandatory)][string]$BuildManifest,[Parameter(Mandatory)][string]$ScopedReceipt)
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
function ReadEvidence([string]$relative) {
    if (!$relative -or [IO.Path]::IsPathRooted($relative)) { throw 'Project-relative evidence path required.' }
    $path=[IO.Path]::GetFullPath((Join-Path $project $relative))
    $base=Join-Path $project 'evidence'
    if (!$path.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence escaped project evidence directory.' }
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked evidence rejected.' }
    return $path
}
$m=Get-Content -LiteralPath $BuildManifest -Raw | ConvertFrom-Json
$r=Get-Content -LiteralPath $ScopedReceipt -Raw | ConvertFrom-Json
if ($r.schema -ne 'starfall.survival-today-scoped-receipt.v1') { throw 'Unsupported scoped survival receipt schema.' }
$content=& (Join-Path $PSScriptRoot 'get-integrated-content-hash.ps1') -BuildDirectory (Join-Path $project ('Builds/'+$m.buildId))
if ($r.build -ne $m.buildId -or $r.sourceCommit -ne $m.sourceCommit -or $r.buildContentSha256 -ne $content.sha256) { throw 'Survival receipt/build identity mismatch.' }
$normal=$r.ordinaryNormalPlay
$p=Get-Content -LiteralPath (ReadEvidence $normal.process) -Raw | ConvertFrom-Json
if ($p.schema -ne 'starfall.normal-process-observation.v2' -or $p.build -ne $m.buildId -or $p.buildContentSha256 -ne $content.sha256) { throw 'Fresh full-content-bound normal process observation required.' }
if (!$p.normalFlagsVerified -or !$p.flags.npcSurvivalRuntime -or $p.flags.npcSurvivalDeathAcceptance -or $p.flags.integratedSmoke -or $p.flags.npcSmoke -or $p.flags.npcRealProbe) { throw 'Ordinary survival process flags required; diagnostics do not count.' }
if ($normal.durationSecondsAtFinalLiveSample -lt 300 -or $normal.buildContentSha256AfterExit -ne $content.sha256) { throw 'At least five minutes ordinary play and matching post-exit content required.' }
$events=@(Get-Content -LiteralPath (ReadEvidence $normal.evidence) | ForEach-Object { $_ | ConvertFrom-Json })
if (!$events.Count) { throw 'Empty ordinary survival event stream.' }
$world=$events[0].world; $actor=$events[0].actor
if (!$world -or !$actor -or @($events | Where-Object { $_.world -ne $world -or $_.actor -ne $actor }).Count) { throw 'Mixed or missing world/inhabitant scope.' }
$decisions=@($events | Where-Object { $_.kind -eq 'decision' -and $_.code -eq 'live-admitted' })
if ($decisions.Count -lt 3 -or @($decisions | Where-Object { $_.finishReason -ne 'stop' -or $_.requestHash -notmatch '^[0-9a-f]{64}$' -or $_.responseHash -notmatch '^[0-9a-f]{64}$' }).Count) { throw 'Genuine complete model-choice provenance missing.' }
if (!@($decisions | Where-Object { $_.choice -like 'explore *' }).Count -or @($events | Where-Object { $_.kind -eq 'route' -and $_.code -eq 'reached' }).Count -lt 3) { throw 'Ordinary exploration choices and completed routes required.' }
foreach($choice in @('eat fruit','drink spring')) {
    $outcomes=@($events | Where-Object { $_.kind -eq 'food' -and $_.choice -eq $choice -and ($_.foodDelta -gt 0 -or $_.waterDelta -gt 0) })
    if (!$outcomes.Count) { throw "No measured ordinary outcome: $choice" }
    foreach($outcome in $outcomes) {
        if (!@($decisions | Where-Object { $_.requestHash -eq $outcome.requestHash -and $_.responseHash -eq $outcome.responseHash -and $_.choice -eq $choice -and $_.tick -le $outcome.tick }).Count) { throw 'Food outcome lacks matching admitted model choice.' }
    }
}
$death=$r.acceleratedDeathDiagnostic
if ($death.buildContentSha256AfterExit -ne $content.sha256) { throw 'Death diagnostic post-exit content fingerprint required.' }
$dp=Get-Content -LiteralPath (ReadEvidence $death.process) -Raw | ConvertFrom-Json
if ($dp.schema -ne 'starfall.normal-process-observation.v2' -or $dp.build -ne $m.buildId -or $dp.buildContentSha256 -ne $content.sha256 -or !$dp.flags.npcSurvivalDeathAcceptance) { throw 'Same-build explicit death diagnostic process required.' }
$d=Get-Content -LiteralPath (ReadEvidence $death.evidence) -Raw | ConvertFrom-Json
if ($d.status -ne 'PASS_COMPILED_ACCELERATED_CAUSE_AND_SAFE_RETURN_NOT_NATURAL_PACING' -or $d.world -ne $world -or $d.actor -ne $actor -or !$d.geometryPreserved) { throw 'Scoped cause diagnostic missing or mismatched.' }
foreach($cause in @('prolonged-dehydration','prolonged-starvation')) {
    $cases=@($d.cases | Where-Object { $_.cause -eq $cause })
    if ($cases.Count -ne 1) { throw 'Exact cause case required.' }
    $c=$cases[0]
    if (!$c.realPhysiologyDeath -or !$c.safeRefugeReturn -or !$c.scopedReload -or !$c.worldAndActorPreserved -or !$c.noInventedKnowledge -or $c.deathHash -notmatch '^[0-9a-f]{64}$' -or $c.incarnationAfter -ne ($c.incarnationBefore+1)) { throw 'Incomplete cause, return, or scoped reload proof.' }
}
[pscustomobject]@{status='TECHNICAL_SURVIVAL_RETENTION_GATE_PASS_NOT_RELEASE';build=$m.buildId;content=$content.sha256;remaining='Natural-timeline death, visual/user acceptance and release remain separate.'} | ConvertTo-Json
