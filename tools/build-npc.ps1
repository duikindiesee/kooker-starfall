param([ValidateSet('Npc','Hybrid')][string]$Mode='Hybrid',[switch]$Smoke)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskEditor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe'
if(Get-Process Unity -ErrorAction SilentlyContinue){throw 'Close the Unity editor before an isolated batch build.'}
$taskKind=$Mode.ToLowerInvariant()
$taskEvidence=Join-Path $taskRoot ('evidence/local/'+$taskKind+'/build-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$null=New-Item -ItemType Directory -Path $taskEvidence
$taskSettings=@{}
foreach($taskName in @('GraphicsSettings.asset','QualitySettings.asset','ProjectSettings.asset')){
    $taskPath=Join-Path $taskRoot ('ProjectSettings/'+$taskName)
    $taskSettings[$taskPath]=[IO.File]::ReadAllBytes($taskPath)
}
Push-Location $taskRoot
try{
    $taskSource=(& git rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0 -or (& git status --porcelain)){throw 'Commit reviewed source before a versioned build.'}
    $taskArgs='-batchmode -quit -projectPath "'+$taskRoot+'" -executeMethod CityLife.World.Editor.CharacterPreviewBuild.Run'+$Mode+' -logFile "'+(Join-Path $taskEvidence 'editor.log')+'"'
    $taskProcess=Start-Process -FilePath $taskEditor -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    Write-Output ('EDITOR_PID: '+$taskProcess.Id)
    if(-not $taskProcess.WaitForExit(600000)){Stop-Process -Id $taskProcess.Id -Force;throw 'Isolated editor timed out.'}
    if($taskProcess.ExitCode -ne 0){throw 'Build failed; inspect retained editor log.'}
    $taskBuild=Get-Content -LiteralPath ('evidence/local/'+$taskKind+'/build.json') -Raw | ConvertFrom-Json
    if($taskBuild.status -ne 'Succeeded'){throw 'Successful build record required.'}
    $taskBuild | Add-Member sourceCommit $taskSource
    $taskBuild | Add-Member library (Get-Content docs/CHARACTER-ASSET-REVISION.json -Raw | ConvertFrom-Json)
    $taskBuild | ConvertTo-Json -Depth 15 | Set-Content (Join-Path $taskRoot ((Split-Path $taskBuild.output)+'/BUILD-MANIFEST.json')) -Encoding utf8NoBOM
    $taskBuild | ConvertTo-Json -Depth 15 | Set-Content (Join-Path $taskEvidence 'build.json') -Encoding utf8NoBOM
    if($Smoke){
        $taskRuntime=Join-Path $taskEvidence 'runtime'
        $taskExe=Join-Path $taskRoot $taskBuild.output
        $taskArgs='-batchmode -force-d3d11 -npcSmoke -npcEvidence "'+$taskRuntime+'" -logFile "'+(Join-Path $taskEvidence 'player.log')+'"'
        $taskPlayer=Start-Process -FilePath $taskExe -ArgumentList $taskArgs -WorkingDirectory (Split-Path $taskExe) -WindowStyle Hidden -PassThru
        Write-Output ('SMOKE_PID: '+$taskPlayer.Id)
        if(-not $taskPlayer.WaitForExit(270000)){Stop-Process -Id $taskPlayer.Id -Force;throw 'Isolated smoke timed out.'}
        if($taskPlayer.ExitCode -ne 0){throw 'Player acceptance failed; inspect retained evidence.'}
        $taskReport=Get-Content (Join-Path $taskRuntime 'npc-runtime.json') -Raw | ConvertFrom-Json
        if($taskReport.status -ne 'PASS'){throw 'Actual player report did not pass.'}
    }
    Write-Output ('NPC_BUILD: '+$taskBuild.output)
    Write-Output ('NPC_EVIDENCE: '+$taskEvidence)
}
finally{
    foreach($taskPath in $taskSettings.Keys){[IO.File]::WriteAllBytes($taskPath,$taskSettings[$taskPath])}
    Pop-Location
}
