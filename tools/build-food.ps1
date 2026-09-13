param()
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if(Get-Process Unity -ErrorAction SilentlyContinue){throw 'Another Unity editor is active. Honor the coordinated build queue before retrying.'}
$taskEvidence=Join-Path $taskRoot ('evidence/local/build-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$null=New-Item -ItemType Directory -Path $taskEvidence
$taskBackups=@{}
foreach($taskName in @('ProjectSettings/GraphicsSettings.asset','ProjectSettings/QualitySettings.asset','ProjectSettings/ProjectSettings.asset','Assets/CityLife/Scenes/Island.unity')){
    $taskPath=Join-Path $taskRoot $taskName
    $taskBackups[$taskPath]=[IO.File]::ReadAllBytes($taskPath)
}
Push-Location $taskRoot
try{
    $taskCommit=(& git rev-parse HEAD).Trim()
    if((& git status --porcelain)){throw 'Commit the reviewed food source before the versioned bake.'}
    $taskArgs='-batchmode -quit -projectPath "'+$taskRoot+'" -executeMethod Starfall.Food.Editor.FoodBuild.Run -logFile "'+(Join-Path $taskEvidence 'editor.log')+'"'
    $taskProcess=Start-Process -FilePath 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    Write-Output ('FOOD_EDITOR_PID '+$taskProcess.Id)
    $taskDeadline=[DateTime]::UtcNow.AddMinutes(12)
    while(-not $taskProcess.WaitForExit(1000)){
        if([DateTime]::UtcNow -gt $taskDeadline){Stop-Process -Id $taskProcess.Id -Force;throw 'Only this food editor was stopped after its build deadline.'}
    }
    if($taskProcess.ExitCode -ne 0){throw 'Food build failed; retain editor log.'}
    $taskReport=Get-Content 'evidence/local/food-build.json' -Raw | ConvertFrom-Json
    if($taskReport.status -ne 'Succeeded'){throw 'No successful food build report.'}
    $taskReport | Add-Member sourceCommit $taskCommit
    $taskReport | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskEvidence 'food-build.json')
    $taskReport | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskRoot ($taskReport.folder+'/BUILD-MANIFEST.json'))
    Write-Output ('FOOD_PLAYER '+(Join-Path $taskRoot ($taskReport.folder+'/StarfallFood.exe')))
    Write-Output ('FOOD_EVIDENCE '+$taskEvidence)
}
finally{
    foreach($taskPath in $taskBackups.Keys){[IO.File]::WriteAllBytes($taskPath,$taskBackups[$taskPath])}
    Pop-Location
}
