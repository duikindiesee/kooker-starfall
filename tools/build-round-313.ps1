param(
    [string]$Editor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe',
    [string]$Round='round-313'
)
$ErrorActionPreference='Stop'
$candidateRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (@(Get-Process Unity -ErrorAction SilentlyContinue).Count) {
    throw 'Another Unity process is active; coordinating...'
}
$candidateLogDir=Join-Path $candidateRoot 'evidence/local/integrated'
$null=New-Item -ItemType Directory -Force -Path $candidateLogDir
$candidateStamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$candidateLog=Join-Path $candidateLogDir ('build-'+$Round+'-'+$candidateStamp+'.log')
$candidateCommit=(git -C $candidateRoot rev-parse HEAD).Trim()

$candidateSettings=@('ProjectSettings/ProjectSettings.asset','ProjectSettings/GraphicsSettings.asset','ProjectSettings/QualitySettings.asset','Assets/Settings/UniversalRenderPipelineGlobalSettings.asset')
$candidateBefore=@{}
foreach ($relative in $candidateSettings) { $path=Join-Path $candidateRoot $relative; if (Test-Path -LiteralPath $path) { $candidateBefore[$relative]=[IO.File]::ReadAllBytes($path) } }

$watch=[Diagnostics.Stopwatch]::StartNew()
$targetRoundDir = Join-Path $candidateRoot ('evidence/milestones/coastal/' + $Round)
if (Test-Path -LiteralPath $targetRoundDir) {
    $existing = @(Get-ChildItem -Path $targetRoundDir)
    if ($existing.Count -gt 0) {
        $priorDir = Join-Path $candidateRoot ('evidence/milestones/coastal/' + $Round + '-' + $candidateStamp + '-prior')
        Write-Output "Preserving prior $Round captures into $priorDir..."
        Move-Item -Path $targetRoundDir -Destination $priorDir -Force
    }
}

try {
    $candidateArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$candidateRoot+'"'),'-executeMethod','CityLife.World.Editor.IntegratedCoastalBuild.Run','-starfallIntegrated','-kokerboomRound',$Round,'-kokerboomSeed','4242','-kokerboomWidth','1600','-ph02FoliageTint','1','-previewSourceCommit',$candidateCommit,'-logFile',('"'+$candidateLog+'"'))
    $candidateProcess=Start-Process -FilePath $Editor -ArgumentList $candidateArgs -WindowStyle Hidden -PassThru
    Write-Output ('Unity PID '+$candidateProcess.Id+' | '+$candidateLog)
    $candidateProcess.WaitForExit()
    $watch.Stop()
    if ($candidateProcess.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $candidateLog) {
            Get-Content -LiteralPath $candidateLog -Tail 50 | Out-Host
        }
        throw ('Build failed with exit code '+$candidateProcess.ExitCode+': '+$candidateLog)
    }
    Write-Output ("BUILD_SUCCESS in "+[Math]::Round($watch.Elapsed.TotalSeconds, 1)+'s!')
    $evidencePath=Join-Path $candidateRoot ('evidence/milestones/coastal/'+$Round+'/preview-build.json')
    if (Test-Path -LiteralPath $evidencePath) {
        $evidenceRaw = Get-Content -LiteralPath $evidencePath -Raw
        $evidenceObj = $evidenceRaw | ConvertFrom-Json

        # Find compiled executable and compute SHA256
        $exeCandidates = Get-ChildItem -Path (Join-Path $candidateRoot 'Builds') -Recurse -Filter 'KookerStarfallIntegrated.exe' | Sort-Object LastWriteTime -Descending
        $binarySha = if ($exeCandidates.Count -gt 0) { (Get-FileHash -LiteralPath $exeCandidates[0].FullName -Algorithm SHA256).Hash } else { "not-found" }
        $binaryPath = if ($exeCandidates.Count -gt 0) { $exeCandidates[0].FullName } else { "" }

        # Git status dirty state
        $gitStatus = (git -C $candidateRoot status --porcelain)
        $isDirty = ($gitStatus.Count -gt 0)

        # Source files SHA256 manifest
        $sourceManifest = [ordered]@{}
        $sourceFiles = Get-ChildItem -Path (Join-Path $candidateRoot 'Assets') -Recurse -Include '*.cs','*.shader','*.hlsl','*.compute' | Sort-Object FullName
        foreach ($sf in $sourceFiles) {
            $rel = $sf.FullName.Substring($candidateRoot.Length).TrimStart('\', '/')
            $sourceManifest[$rel] = (Get-FileHash -LiteralPath $sf.FullName -Algorithm SHA256).Hash
        }
        $toolFiles = Get-ChildItem -Path (Join-Path $candidateRoot 'tools') -Recurse -Include '*.ps1' | Sort-Object FullName
        foreach ($tf in $toolFiles) {
            $rel = $tf.FullName.Substring($candidateRoot.Length).TrimStart('\', '/')
            $sourceManifest[$rel] = (Get-FileHash -LiteralPath $tf.FullName -Algorithm SHA256).Hash
        }

        # Add provenance properties
        $evidenceObj | Add-Member -NotePropertyName 'commit' -NotePropertyValue $candidateCommit -Force
        $evidenceObj | Add-Member -NotePropertyName 'binarySha256' -NotePropertyValue $binarySha -Force
        $evidenceObj | Add-Member -NotePropertyName 'binaryPath' -NotePropertyValue $binaryPath -Force
        $evidenceObj | Add-Member -NotePropertyName 'gitDirty' -NotePropertyValue $isDirty -Force
        $evidenceObj | Add-Member -NotePropertyName 'gitDirtyDetails' -NotePropertyValue @($gitStatus) -Force
        $evidenceObj | Add-Member -NotePropertyName 'sourceFileCount' -NotePropertyValue $sourceManifest.Count -Force
        $evidenceObj | Add-Member -NotePropertyName 'sourceFileManifest' -NotePropertyValue $sourceManifest -Force

        $enrichedJson = $evidenceObj | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText($evidencePath, $enrichedJson)
        Write-Output "Enriched preview-build.json with binary SHA256 ($binarySha), git dirty ($isDirty), and $($sourceManifest.Count) source file hashes."
        $enrichedJson | Out-Host
    }
} finally {
    if (@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { $_.CommandLine -like ('*'+$candidateRoot+'*') }).Count -eq 0) {
        foreach ($relative in $candidateBefore.Keys) { [IO.File]::WriteAllBytes((Join-Path $candidateRoot $relative),$candidateBefore[$relative]) }
    }
}
