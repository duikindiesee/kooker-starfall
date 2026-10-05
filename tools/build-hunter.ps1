param([switch]$Verify)
$ErrorActionPreference='Stop'
$hunterRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$hunterEditor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe'
if(Get-Process Unity -ErrorAction SilentlyContinue){throw 'Another Unity editor is running; do not interrupt it.'}
if(-not(Test-Path -LiteralPath $hunterEditor)){throw 'Pinned Unity 6000.6.0f1 is required.'}
$hunterStamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$hunterEvidence=Join-Path $hunterRoot ('evidence\local\hunter\build-'+$hunterStamp)
$null=New-Item -ItemType Directory -Path $hunterEvidence
$hunterRevision=(& git -C $hunterRoot rev-parse HEAD).Trim()
if(& git -C $hunterRoot status --porcelain){throw 'Commit the reviewed source before producing a versioned preview.'}
$hunterSettings=@{}
foreach($hunterName in @('GraphicsSettings.asset','QualitySettings.asset','ProjectSettings.asset')){
 $hunterPath=Join-Path $hunterRoot ('ProjectSettings\'+$hunterName)
 $hunterSettings[$hunterPath]=[IO.File]::ReadAllBytes($hunterPath)
}
$hunterArgs='-batchmode -quit -projectPath "'+$hunterRoot+'" -executeMethod CityLife.World.Editor.CharacterPreviewBuild.RunHunter -logFile "'+(Join-Path $hunterEvidence 'editor.log')+'"'
try {
$hunterProcess=Start-Process -FilePath $hunterEditor -ArgumentList $hunterArgs -WindowStyle Hidden -PassThru
$hunterProcess.WaitForExit()
if($hunterProcess.ExitCode -ne 0){throw ('Build failed; see '+$hunterEvidence)}
} finally {foreach($hunterPath in $hunterSettings.Keys){[IO.File]::WriteAllBytes($hunterPath,$hunterSettings[$hunterPath])}}
$hunterBuild=Get-Content -LiteralPath (Join-Path $hunterRoot 'evidence\local\character\build.json') -Raw | ConvertFrom-Json
if($hunterBuild.status -ne 'Succeeded' -or $hunterBuild.buildId -notlike 'KookerStarfallHunter-*'){throw 'Missing successful hunter build record.'}
$hunterExe=Join-Path $hunterRoot $hunterBuild.output
$hunterBuild | Add-Member -NotePropertyName sourceCommit -NotePropertyValue $hunterRevision
$hunterBuild | Add-Member -NotePropertyName executableSha256 -NotePropertyValue (Get-FileHash -LiteralPath $hunterExe -Algorithm SHA256).Hash.ToLowerInvariant()
$hunterBuild | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $hunterEvidence 'build.json')
$hunterBuild | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path (Split-Path $hunterExe) 'HUNTER-BUILD-MANIFEST.json')
Write-Output ('HUNTER_PREVIEW: '+$hunterExe)
if($Verify){
 $hunterArgs='-force-d3d11 -screen-fullscreen 0 -hunterVerify -hunterEvidence "'+(Join-Path $hunterEvidence 'runtime')+'" -logFile "'+(Join-Path $hunterEvidence 'player.log')+'"'
 $hunterPlayer=Start-Process -FilePath $hunterExe -ArgumentList $hunterArgs -WorkingDirectory (Split-Path $hunterExe) -WindowStyle Normal -PassThru
 $hunterPlayer.WaitForExit()
 if($hunterPlayer.ExitCode -ne 0){throw 'Hunter motion capture failed.'}
 Write-Output ('Review captured motion frames in '+$hunterEvidence+'; machine completion is not visual clipping acceptance.')
}
