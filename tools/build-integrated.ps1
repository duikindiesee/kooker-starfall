param(
    [string]$Editor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe',
    [ValidatePattern('^round-[0-9]{2,3}$')][string]$Round='round-100',
    [Parameter(Mandatory)][string]$VerifiedFallbackManifest,
    [Parameter(Mandatory)][string]$VerifiedFallbackRuntime
)
$ErrorActionPreference='Stop'
$candidateRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (@(Get-Process Unity -ErrorAction SilentlyContinue).Count) { throw 'Another owner has the Unity build slot; coordinate before running.' }
if (@(git -C $candidateRoot status --porcelain).Count) { throw 'Commit and review candidate source before building.' }
if (!(Test-Path -LiteralPath (Join-Path $candidateRoot 'Assets/CityLife/Editor/HunterOutfitAuthoring.cs'))) { throw 'Accepted clothing source is required.' }
$fallback=(& (Join-Path $PSScriptRoot 'check-integrated-build.ps1') -BuildManifest $VerifiedFallbackManifest -RuntimeDirectory $VerifiedFallbackRuntime | ConvertFrom-Json)
if($fallback.status -ne 'IDENTITY_AND_AUTOMATED_RECEIPTS_MATCH_NOT_RELEASE_ACCEPTANCE'){throw 'Verified fallback required before build.'}
$candidateOutput=Join-Path $candidateRoot ('evidence/milestones/coastal/'+$Round)
if (Test-Path -LiteralPath $candidateOutput) { throw 'Choose an unused evidence round; existing results are preserved.' }
$candidateLogDir=Join-Path $candidateRoot 'evidence/local/integrated'
$null=New-Item -ItemType Directory -Force -Path $candidateLogDir
$candidateStamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$candidateLog=Join-Path $candidateLogDir ('build-'+$candidateStamp+'.log')
$candidateCommit=(git -C $candidateRoot rev-parse HEAD).Trim()
$candidateSettings=@('ProjectSettings/ProjectSettings.asset','ProjectSettings/GraphicsSettings.asset','ProjectSettings/QualitySettings.asset','Assets/Settings/UniversalRenderPipelineGlobalSettings.asset')
$candidateBefore=@{}
foreach ($relative in $candidateSettings) { $path=Join-Path $candidateRoot $relative; if (Test-Path -LiteralPath $path) { $candidateBefore[$relative]=[IO.File]::ReadAllBytes($path) } }
try {
    $candidateArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$candidateRoot+'"'),'-executeMethod','CityLife.World.Editor.IntegratedCoastalBuild.Run','-starfallIntegrated','-kokerboomRound',$Round,'-kokerboomSeed','4242','-kokerboomWidth','1600','-ph02FoliageTint','1','-previewSourceCommit',$candidateCommit,'-logFile',('"'+$candidateLog+'"'))
    $candidateProcess=Start-Process -FilePath $Editor -ArgumentList $candidateArgs -WindowStyle Hidden -PassThru
    Write-Output ('Integrated Unity PID '+$candidateProcess.Id+' | '+$candidateLog)
    $candidateProcess.WaitForExit()
    if ($candidateProcess.ExitCode -ne 0) { throw ('Integrated build failed: '+$candidateLog) }
    $candidateEvidence=Get-Content -Raw -LiteralPath (Join-Path $candidateOutput 'preview-build.json') | ConvertFrom-Json
    if ($candidateEvidence.status -ne 'Succeeded' -or $candidateEvidence.sourceCommit -ne $candidateCommit -or $candidateEvidence.version -ne '0.0.11-survival.1') { throw 'Build evidence does not match this candidate.' }
    # The new player is kept as an UNVERIFIED temporary candidate. Prune only
    # manifest-known superseded outputs after the verified fallback is rechecked;
    # this is disk lifecycle, not survival promotion or release acceptance.
    $retained=(& (Join-Path $PSScriptRoot 'reconcile-integrated-builds.ps1') `
        -FallbackManifest $VerifiedFallbackManifest -FallbackRuntime $VerifiedFallbackRuntime `
        -CandidateManifest (Join-Path $candidateOutput 'preview-build.json') `
        -TemporaryCandidate -Execute)
    if($retained.status -ne 'RETAINED_VERIFIED_FALLBACK_AND_TEMPORARY_CANDIDATE_NOT_RELEASE'){
        throw 'Automatic two-keep reconciliation did not finish.'
    }
    [ordered]@{status=$candidateEvidence.status;source=$candidateCommit;player=(Join-Path $candidateRoot $candidateEvidence.output);
        evidence=$candidateOutput;log=$candidateLog;runtime='UNVERIFIED';
        retentionStatus='VERIFIED_FALLBACK_AND_TEMPORARY_CANDIDATE_NOT_RELEASE';retentionAudit=$retained.auditPath} | ConvertTo-Json
} catch {
    $failedManifest=Join-Path $candidateOutput 'preview-build.json'
    if (Test-Path -LiteralPath $failedManifest) {
        try {
            $failedReceipt=Get-Content -LiteralPath $failedManifest -Raw | ConvertFrom-Json
            if ($failedReceipt.status -eq 'Failed') {
                & (Join-Path $PSScriptRoot 'remove-failed-integrated-build.ps1') -Manifest $failedManifest -Execute | Out-Host
            }
        } catch { Write-Warning ('Failed-output cleanup deferred: '+$_.Exception.Message) }
    }
    throw
} finally {
    if (@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { $_.CommandLine -like ('*'+$candidateRoot+'*') }).Count -eq 0) {
        foreach ($relative in $candidateBefore.Keys) { [IO.File]::WriteAllBytes((Join-Path $candidateRoot $relative),$candidateBefore[$relative]) }
    }
}
