[CmdletBinding()]
param(
    [ValidatePattern('^round-[0-9]{2,3}$')][string]$Round = 'round-01',
    [int]$Seed = 4242,
    [ValidateRange(800,3840)][int]$Width = 1600,
    [switch]$ImportedCandidates,
    [switch]$Hybrid,
    [switch]$PlayablePreview,
    [switch]$R19PlayablePreview,
    [switch]$PH02Crown,
    [switch]$PH01Tangents,
    [switch]$PH02Fitted,
    [switch]$PH02Family,
    [string]$PH02Shots,
    [ValidateRange(0,1)][float]$PH02FoliageTint=0,
    [switch]$PH02ImportedTuples,
    [switch]$WoodDiagnostic,
    [switch]$ExactTupleDedup,
    [ValidateRange(0.001,1.419)][float]$PH02CutHeight = 0.65,
    [string]$EditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe',
    [ValidateRange(60,3600)][int]$TimeoutSeconds = 900
)
$ErrorActionPreference = 'Stop'
$projectPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath (Join-Path $projectPath 'Assets\CityLife\Editor\KokerboomRender.cs'))) {
    throw 'This script must run from the cosmic CityLife worktree containing KokerboomRender.cs.'
}
if (-not (Test-Path -LiteralPath $EditorPath)) { throw "Pinned Unity editor was not found at $EditorPath" }
$runningEditors = @(Get-Process -Name Unity -ErrorAction SilentlyContinue)
if ($runningEditors.Count -gt 0) {
    throw 'A Unity editor is already running. Finish the coordinated editor operation before starting this isolated render; this script will not interrupt it.'
}
$outputDirectory = Join-Path $projectPath "evidence\milestones\kokerboom\$Round"
if ((Test-Path -LiteralPath $outputDirectory) -and @(Get-ChildItem -LiteralPath $outputDirectory -File).Count -gt 0) {
    throw "Evidence already exists in $Round. Choose a new round; existing captures are preserved."
}
$localLogs = Join-Path $projectPath 'evidence\local'
New-Item -ItemType Directory -Force -Path $localLogs | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$logPath = Join-Path $localLogs "kokerboom-$Round-$stamp.log"
if(([int]$ImportedCandidates.IsPresent+[int]$Hybrid.IsPresent+[int]$PlayablePreview.IsPresent+[int]$R19PlayablePreview.IsPresent+[int]$PH02Crown.IsPresent+[int]$PH01Tangents.IsPresent+[int]$PH02Fitted.IsPresent+[int]$WoodDiagnostic.IsPresent+[int]$PH02Family.IsPresent) -gt 1){throw 'Select one inspection mode.'}
$previewSourceCommit=''
if($R19PlayablePreview){
    if($Seed -ne 4242){throw 'Frozen R19 preview requires seed4242.'}
    $previewSourceCommit=(& git -C $projectPath rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0 -or $previewSourceCommit -notmatch '^[0-9a-f]{40}$'){throw 'Cannot resolve exact preview source commit.'}
    $sourceChanges=@(& git -C $projectPath status --porcelain -- Assets Packages ProjectSettings tools)
    if($LASTEXITCODE -ne 0 -or $sourceChanges.Count){throw 'Commit the preview source before baking, so the player maps to an exact source commit.'}
    $frozen=Get-Content -LiteralPath (Join-Path $projectPath 'evidence/verified/starfall-tree-baseline.json') -Raw | ConvertFrom-Json
    foreach($inputFile in $frozen.frozenR19Candidate.sourceFiles){
        if($inputFile.source -in @('Assets/CityLife/Editor/KokerboomRender.cs','tools/render-kokerboom.ps1')){continue}
        if((Get-FileHash -LiteralPath (Join-Path $projectPath $inputFile.source) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $inputFile.sha256){throw "Frozen R19 input changed: $($inputFile.source)"}
    }
}
# Optional PH02 shot subset; all existing 21 shots remain the default.
$ph02ShotSelectionRequested=$PSBoundParameters.ContainsKey('PH02Shots')
$ph02SelectedNumbers=@()
if($ph02ShotSelectionRequested){
    if(-not $PH02Family){throw 'PH02Shots applies only to PH02Family.'}
    if([string]::IsNullOrWhiteSpace($PH02Shots)){throw 'PH02Shots must contain comma-separated two-digit shot numbers, e.g. 05,08,13,18,20,21.'}
    $ph02SelectedNumbers=@($PH02Shots.Split(',') | ForEach-Object { $_.Trim() })
    if(@($ph02SelectedNumbers | Where-Object { $_ -notmatch '^(0[1-9]|1[0-9]|2[01])$' }).Count){throw 'PH02Shots must name existing PH02 family shots 01 through 21.'}
    if(@($ph02SelectedNumbers | Select-Object -Unique).Count -ne $ph02SelectedNumbers.Count){throw 'PH02Shots must not contain duplicate shot numbers.'}
    $ph02SelectedNumbers=@($ph02SelectedNumbers | Sort-Object)
}
# End optional PH02 selection validation.
# Reference fields are compared after capture, not replayed. This does not
# assert matrix equality: R16 records camera settings, not view/projection matrices.
$ph02CameraReference=$null
if($ph02ShotSelectionRequested){
    $ph02CameraReferencePath=Join-Path $projectPath 'evidence/milestones/kokerboom/round-16/metrics.json'
    $ph02CameraReferenceHash=(Get-FileHash -LiteralPath $ph02CameraReferencePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $ph02CameraReference=Get-Content -LiteralPath $ph02CameraReferencePath -Raw | ConvertFrom-Json
    if(-not $ph02CameraReference.technicalChecksPassed -or @($ph02CameraReference.captures).Count -ne 21){throw 'The recorded R16 camera catalogue is required for scoped PH02 comparisons.'}
}
function Get-PH02CameraDifference {
    param($Actual,$Reference)
    $different=@();$deltas=[ordered]@{}
    foreach($group in @('position','rotationEuler')){foreach($axis in @('x','y','z')){
        $a=[double]$Actual.camera.$group.$axis;$b=[double]$Reference.camera.$group.$axis
        $name="$group.$axis";$deltas[$name]=$a-$b;if($a -ne $b){$different+=$name}
    }}
    foreach($name in @('orthographicSizeMetres','fieldOfView','nearClip','farClip')){
        $a=[double]$Actual.camera.$name;$b=[double]$Reference.camera.$name
        $deltas[$name]=$a-$b;if($a -ne $b){$different+=$name}
    }
    $projectionSame=$Actual.camera.orthographic -eq $Reference.camera.orthographic
    if(-not $projectionSame){$different+='orthographic'}
    $dimensionsSame=$Actual.widthPixels -eq $Reference.widthPixels -and $Actual.heightPixels -eq $Reference.heightPixels
    if(-not $dimensionsSame){$different+='imageDimensions'}
    return [pscustomobject]@{id=$Actual.id;recordedFieldsExactlyEqual=$different.Count -eq 0;differentFields=@($different);signedActualMinusReference=$deltas;projectionModeSame=$projectionSame;imageDimensionsSame=$dimensionsSame}
}
if($PSBoundParameters.ContainsKey('PH02FoliageTint') -and -not $PH02Family){throw 'PH02FoliageTint applies only to the PH02Family comparison.'}
if($ExactTupleDedup -and -not $PH01Tangents){throw 'ExactTupleDedup applies only to the PH01Tangents comparison.'}
if($PH02ImportedTuples -and -not $PH02Fitted){throw 'PH02ImportedTuples applies only to PH02Fitted; the default source path is unchanged.'}
if($PH02ImportedTuples -and $Width -ne 1600){throw 'PH02ImportedTuples retains the preserved R13 camera framing and 1600-pixel image width.'}
if($PH02Fitted -and $PSBoundParameters.ContainsKey('PH02CutHeight')){throw 'PH02Fitted uses the recorded source cut at0.65m; the free cut parameter belongs to PH02Crown.'}
if($WoodDiagnostic -and $Seed -ne 4242){throw 'WoodDiagnostic preserves the R09 seed4242 main specimen.'}
$entryPoint = if($PH02Family) { 'CityLife.World.Editor.KokerboomRender.RenderPH02Family' } elseif($WoodDiagnostic) { 'CityLife.World.Editor.KokerboomRender.RenderWoodDiagnostic' } elseif($PH02Fitted) { 'CityLife.World.Editor.KokerboomRender.RenderPH02FittedSupport' } elseif($PH01Tangents) { 'CityLife.World.Editor.KokerboomRender.RenderPH01TangentComparison' } elseif($PH02Crown) { 'CityLife.World.Editor.KokerboomRender.RenderPH02CrownCandidate' } elseif($PlayablePreview) { 'CityLife.World.Editor.KokerboomRender.BuildPlayablePreview' } elseif ($ImportedCandidates) { 'CityLife.World.Editor.KokerboomRender.RenderImportedCandidates' } elseif($Hybrid) { 'CityLife.World.Editor.KokerboomRender.RenderHybridFamily' } else { 'CityLife.World.Editor.KokerboomRender.RenderBatch' }
$expectedCaptures = if($ph02ShotSelectionRequested) { $ph02SelectedNumbers.Count } elseif($PH02Family) { 21 } elseif($WoodDiagnostic) { 3 } elseif($PH02Fitted) { 12 } elseif($PH01Tangents -or $PH02Crown) { 8 } elseif($PlayablePreview) { 2 } elseif ($ImportedCandidates) { 12 } elseif($Hybrid) { 21 } else { 15 }
if($R19PlayablePreview){$entryPoint='CityLife.World.Editor.KokerboomRender.BuildR19PlayablePreview';$expectedCaptures=2}
$arguments = @('-batchmode', '-force-d3d11', '-projectPath', ('"' + $projectPath + '"'),
    '-executeMethod', $entryPoint,
    '-kokerboomRound', $Round, '-kokerboomSeed', $Seed.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-kokerboomWidth', $Width.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-logFile', ('"' + $logPath + '"'))
if($PH02Crown){$arguments+=@('-ph02CutHeight',$PH02CutHeight.ToString('R',[Globalization.CultureInfo]::InvariantCulture))}
if($PH01Tangents){$arguments+=@('-ph01ExactTupleDedup',([int]$ExactTupleDedup.IsPresent).ToString([Globalization.CultureInfo]::InvariantCulture))}
if($PH02Fitted){$arguments+=@('-ph02ImportedTuples',([int]$PH02ImportedTuples.IsPresent).ToString([Globalization.CultureInfo]::InvariantCulture))}
if($PH02Family){$arguments+=@('-ph02FoliageTint',$PH02FoliageTint.ToString('R',[Globalization.CultureInfo]::InvariantCulture))}
if($ph02ShotSelectionRequested){$arguments+=@('-ph02Shots',($ph02SelectedNumbers -join ','))}
if($R19PlayablePreview){$arguments+=@('-previewSourceCommit',$previewSourceCommit)}
# Graphics remain enabled. Batch mode + a hidden process do not activate a desktop editor window.
# The render entry point exits the process itself; -quit and -nographics are deliberately absent.
$renderProcess = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while (-not $renderProcess.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) {
        Stop-Process -Id $renderProcess.Id -Force
        throw "The isolated render exceeded $TimeoutSeconds seconds. Only the process started by this script was stopped. Inspect $logPath"
    }
}
$renderProcess.Refresh()
$exitCode = $renderProcess.ExitCode
$metricsPath = Join-Path $outputDirectory 'metrics.json'
if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $metricsPath)) {
    throw "Kokerboom rendering failed (exit $exitCode). Existing evidence is retained. Inspect $logPath"
}
$report = Get-Content -Raw -LiteralPath $metricsPath | ConvertFrom-Json
if($R19PlayablePreview){
    if($report.mode -ne 'frozen-r19-playable-preview-stage' -or $report.ph02FoliageTintStrength -ne 1 -or -not $report.ph02FamilyChecks.numericChecksPassed -or -not $report.ph02FamilyMeshReadbackPassed){throw 'Frozen R19 component, tint or mode did not pass.'}
    $buildRecord=Get-Content -LiteralPath (Join-Path $outputDirectory 'preview-build.json') -Raw | ConvertFrom-Json
    if($buildRecord.status -ne 'Succeeded' -or $buildRecord.version -ne '0.0.2-preview.2' -or $buildRecord.sourceCommit -ne $previewSourceCommit){throw 'Separate R19 player did not build from the recorded source.'}
}
$expectedPH02Mode=if($ph02ShotSelectionRequested){'hybrid-ph02-fitted-crown-scoped-inspection'}else{'hybrid-ph02-fitted-crown-family-experiment'}
if($PH02Family -and ($report.mode -ne $expectedPH02Mode -or -not $report.ph02FamilyChecks.numericChecksPassed -or -not $report.ph02FamilyChecks.actualImportedGatePassed -or -not $report.ph02FamilyMeshReadbackPassed -or $report.ph02FoliageTintStrength -ne $PH02FoliageTint)){
    throw 'PH02 family component or requested tint did not pass the recorded technical checks. Component checks do not constitute full-family acceptance.'
}
if($PH02Family){
    $actualIds=@($report.captures | ForEach-Object { [string]$_.id })
    $actualNumbers=@($actualIds | ForEach-Object { $_.Substring(0,2) })
    $expectedNumbers=if($ph02ShotSelectionRequested){$ph02SelectedNumbers}else{@(1..21 | ForEach-Object { $_.ToString('00',[Globalization.CultureInfo]::InvariantCulture) })}
    if($report.ph02ShotSelectionRequested -ne $ph02ShotSelectionRequested -or (@($report.ph02SelectedShotIds) -join ',') -cne ($actualIds -join ',') -or ($actualNumbers -join ',') -cne ($expectedNumbers -join ',')){
        throw 'PH02 selected IDs, canonical capture order or scoped/full mode did not match the request. Existing evidence is preserved.'
    }
}
if($PH02Fitted -and ($report.mode -ne 'ph02-fitted-support-comparison' -or -not $report.ph02FittedChecks.numericChecksPassed -or -not $report.ph02FittedReadback.passed)){
    throw "PH02 fitted support failed its actual Create/shared-rim readback checks. Inspect $metricsPath and ph02-fitted-support-checks.json."
}
if($PH02Fitted -and $report.ph02ImportedTuplesRequested -ne $PH02ImportedTuples.IsPresent){throw 'The requested PH02 crown input path was not recorded.'}
if($PH02ImportedTuples -and (-not $report.ph02ImportedCrownChecks.actualUnityImportedData -or -not $report.ph02ImportedCrownChecks.mappingComplete -or -not $report.ph02ImportedCrownChecks.exactExpandedTuplePreservation -or -not $report.ph02ImportedCrownMeshReadbackPassed -or -not $report.ph02FittedChecks.cloneMatchesCaller -or -not $report.ph02FittedChecks.callerCrownUnchanged)){
    throw 'Imported PH02 tuple mapping, actual Mesh expanded hashes or caller/clone preservation did not pass. Existing reports remain available.'
}
if($WoodDiagnostic -and $report.mode -ne 'r09-matched-wood-diagnostic'){throw 'The wood diagnostic mode was not recorded.'}
if($PH01Tangents){
    $expectedCaptures=if($report.originalSubsetImagesIncluded){12}else{8}
    if($report.mode -ne 'ph01-imported-tangent-comparison' -or -not $report.importedTupleChecks.numericChecksPassed -or $report.importedTupleChecks.exactTupleDedup -ne $ExactTupleDedup.IsPresent){
        throw "PH01 tuple checks or requested mode did not match. Numeric evidence is retained in $metricsPath and ph01-imported-tuple-checks.json."
    }
}
if (-not $report.technicalChecksPassed -or $report.expectedCaptures -ne $expectedCaptures -or @($report.captures).Count -ne $expectedCaptures) {
    throw "Render evidence did not pass its technical checks. Inspect $metricsPath and $logPath"
}
$ph02CameraComparisonPath=$null
if($ph02ShotSelectionRequested){
    $comparisons=@(foreach($capture in $report.captures){
        $baseline=@($ph02CameraReference.captures | Where-Object { $_.id -ceq $capture.id })
        if($baseline.Count -ne 1){throw "R16 must contain one camera reference for selected shot $($capture.id). Existing images remain preserved."}
        Get-PH02CameraDifference -Actual $capture -Reference $baseline[0]
    })
    $ph02CameraComparisonPath=Join-Path $outputDirectory 'ph02-shot-camera-comparison.json'
    [pscustomobject]@{
        schema='starfall.ph02-selected-shot-camera-fields.v1'
        scope='Same-view categories, not a replay. Camera functions are unchanged but bounds-dependent fields can differ. Signed raw Euler deltas are not quaternion-angle differences. Inactive orthographic/perspective fields are retained. R16 did not record camera matrices; no matrix equality, lighting equality, geometry parity or full-family acceptance inferred.'
        referenceManifest='evidence/milestones/kokerboom/round-16/metrics.json'
        referenceManifestSha256=$ph02CameraReferenceHash
        actualManifestSha256=(Get-FileHash -LiteralPath $metricsPath -Algorithm SHA256).Hash.ToLowerInvariant()
        selectedShotIds=@($report.ph02SelectedShotIds)
        comparisons=$comparisons
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $ph02CameraComparisonPath -Encoding utf8NoBOM
}
[pscustomobject]@{
    status = if($R19PlayablePreview){'Built separate frozen R19 player; runtime validation remains separate'}elseif($ph02ShotSelectionRequested){'Rendered scoped PH02 inspection; not a full-family review set'}else{'Rendered for independent critique'}
    round = $Round
    images = @($report.captures).Count
    metrics = $metricsPath
    log = $logPath
    cameraComparison = $ph02CameraComparisonPath
    visualAcceptance = if($R19PlayablePreview){'R19 review remains frozen; this is build integration only.'}elseif($ph02ShotSelectionRequested){'Not scored; selected shots are not a full-family evidence set.'}else{'Not scored; independent critique required.'}
    selectedShotIds = if($PH02Family){@($report.ph02SelectedShotIds)}else{@()}
} | ConvertTo-Json
