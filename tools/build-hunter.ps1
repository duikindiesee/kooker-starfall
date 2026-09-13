param([switch]$Verify)
$ErrorActionPreference='Stop'
$hunterRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$hunterEditor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe'
if(Get-Process Unity -ErrorAction SilentlyContinue){throw 'Another Unity editor is running; do not interrupt it.'}
if(-not(Test-Path -LiteralPath $hunterEditor)){throw 'Pinned Unity 6000.6.0f1 is required.'}
$hunterStamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$hunterEvidence=Join-Path $hunterRoot ('evidence\local\hunter\build-'+$hunterStamp)
$null=New-Item -ItemType Directory -Path $hunterEvidence
$hunterArgs='-batchmode -quit -projectPath "'+$hunterRoot+'" -executeMethod CityLife.World.Editor.CharacterPreviewBuild.RunHunter -logFile "'+(Join-Path $hunterEvidence 'editor.log')+'"'
$hunterProcess=Start-Process -FilePath $hunterEditor -ArgumentList $hunterArgs -WindowStyle Hidden -PassThru
$hunterProcess.WaitForExit()
if($hunterProcess.ExitCode -ne 0){throw ('Build failed; see '+$hunterEvidence)}
$hunterBuild=Get-Content -LiteralPath (Join-Path $hunterRoot 'evidence\local\character\build.json') -Raw | ConvertFrom-Json
if($hunterBuild.status -ne 'Succeeded' -or $hunterBuild.buildId -notlike 'KookerStarfallHunter-*'){throw 'Missing successful hunter build record.'}
$hunterBuild | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $hunterEvidence 'build.json')
$hunterExe=Join-Path $hunterRoot $hunterBuild.output
Write-Output ('HUNTER_PREVIEW: '+$hunterExe)
if($Verify){
 $hunterArgs='-force-d3d11 -screen-fullscreen 0 -hunterVerify -hunterEvidence "'+(Join-Path $hunterEvidence 'runtime')+'" -logFile "'+(Join-Path $hunterEvidence 'player.log')+'"'
 $hunterPlayer=Start-Process -FilePath $hunterExe -ArgumentList $hunterArgs -WorkingDirectory (Split-Path $hunterExe) -WindowStyle Normal -PassThru
 $hunterPlayer.WaitForExit()
 if($hunterPlayer.ExitCode -ne 0){throw 'Hunter motion capture failed.'}
 Write-Output ('Review captured motion frames in '+$hunterEvidence+'; machine completion is not visual clipping acceptance.')
}
