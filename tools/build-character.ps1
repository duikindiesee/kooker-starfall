param([switch]$Smoke)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskEditor = 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe'
if (-not (Test-Path -LiteralPath $taskEditor)) { throw 'Pinned Unity editor not found.' }
if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'Close the Unity editor before this isolated batch build.' }
$taskId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$taskEvidence = Join-Path $taskRoot ('evidence\local\character\build-' + $taskId)
$null = New-Item -ItemType Directory -Path $taskEvidence
$taskSettings = @{}
foreach ($name in @('GraphicsSettings.asset', 'QualitySettings.asset', 'ProjectSettings.asset')) {
    $taskPath = Join-Path $taskRoot ('ProjectSettings\' + $name)
    $taskSettings[$taskPath] = [IO.File]::ReadAllBytes($taskPath)
}
Push-Location $taskRoot
try {
    $taskSource = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Source commit unavailable.' }
    if (& git status --porcelain) { throw 'Commit the reviewed source before creating a versioned preview.' }
    $taskArgs = '-batchmode -quit -projectPath "' + $taskRoot + '" -executeMethod CityLife.World.Editor.CharacterPreviewBuild.Run -logFile "' + (Join-Path $taskEvidence 'editor.log') + '"'
    $taskProcess = Start-Process -FilePath $taskEditor -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit(600000)) {
        Stop-Process -Id $taskProcess.Id -Force
        throw 'The isolated editor exceeded ten minutes and was stopped.'
    }
    if ($taskProcess.ExitCode -ne 0) { throw 'Unity build failed; inspect the retained editor log.' }
    $taskBuild = Get-Content -LiteralPath 'evidence/local/character/build.json' -Raw | ConvertFrom-Json
    if ($taskBuild.status -ne 'Succeeded') { throw 'A successful build record is required.' }
    $taskBuild | Add-Member -NotePropertyName sourceCommit -NotePropertyValue $taskSource
    $taskBuild | Add-Member -NotePropertyName library -NotePropertyValue (Get-Content -LiteralPath 'docs/CHARACTER-ASSET-REVISION.json' -Raw | ConvertFrom-Json)
    $taskManifest = Join-Path $taskRoot ((Split-Path $taskBuild.output) + '\BUILD-MANIFEST.json')
    $taskBuild | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $taskManifest -Encoding utf8NoBOM
    $taskBuild | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $taskEvidence 'build.json') -Encoding utf8NoBOM
    if ($Smoke) {
        $taskRuntime = Join-Path $taskEvidence 'runtime'
        $taskExe = Join-Path $taskRoot $taskBuild.output
        $taskArgs = '-batchmode -force-d3d11 -characterSmoke -characterEvidence "' + $taskRuntime + '" -logFile "' + (Join-Path $taskEvidence 'player.log') + '"'
        $taskPlayer = Start-Process -FilePath $taskExe -ArgumentList $taskArgs -WorkingDirectory (Split-Path $taskExe) -WindowStyle Hidden -PassThru
        if (-not $taskPlayer.WaitForExit(210000)) {
            Stop-Process -Id $taskPlayer.Id -Force
            throw 'The isolated smoke player exceeded its limit and was stopped.'
        }
        if ($taskPlayer.ExitCode -ne 0) { throw 'Player verification failed; inspect retained runtime evidence.' }
        $taskReport = Get-Content -LiteralPath (Join-Path $taskRuntime 'character-runtime.json') -Raw | ConvertFrom-Json
        if ($taskReport.status -ne 'PASS') { throw 'Player runtime report did not pass.' }
    }
    Write-Output ('CHARACTER_BUILD: ' + $taskBuild.output)
    Write-Output ('CHARACTER_EVIDENCE: ' + $taskEvidence)
}
finally {
    foreach ($taskPath in $taskSettings.Keys) { [IO.File]::WriteAllBytes($taskPath, $taskSettings[$taskPath]) }
    Pop-Location
}
