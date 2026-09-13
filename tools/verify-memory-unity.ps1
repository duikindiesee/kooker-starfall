param()
$ErrorActionPreference = 'Stop'
$memoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$memoryEditor = 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe'
if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'An existing Unity editor must remain undisturbed; retry this isolated proof after it closes.' }
Push-Location $memoryRoot
$memorySettings = @{}
try {
    $memorySource = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or (& git status --porcelain)) { throw 'Commit the proof source before running Unity.' }
    $memoryRun = Join-Path $memoryRoot ('evidence/local/memory/unity-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    $null = New-Item -ItemType Directory -Path $memoryRun
    foreach ($memoryName in @('ProjectSettings/GraphicsSettings.asset', 'ProjectSettings/QualitySettings.asset', 'ProjectSettings/ProjectSettings.asset', 'Assets/Settings/UniversalRenderPipelineGlobalSettings.asset')) {
        $memoryPath = Join-Path $memoryRoot $memoryName
        $memorySettings[$memoryPath] = [IO.File]::ReadAllBytes($memoryPath)
    }
    $memoryArgs = '-batchmode -quit -projectPath "' + $memoryRoot + '" -executeMethod CityLife.World.Editor.StarfallMemoryValidation.Run -starfallMemoryEvidence "' + (Join-Path $memoryRun 'export') + '" -starfallMemorySource ' + $memorySource + ' -logFile "' + (Join-Path $memoryRun 'editor.log') + '"'
    $memoryProcess = Start-Process -FilePath $memoryEditor -ArgumentList $memoryArgs -WindowStyle Hidden -PassThru
    Write-Output ('UNITY_MEMORY_PID: ' + $memoryProcess.Id)
    $memoryWatch = [Diagnostics.Stopwatch]::StartNew()
    while (-not $memoryProcess.WaitForExit(1000)) {
        if ($memoryWatch.Elapsed.TotalMinutes -gt 12) { Stop-Process -Id $memoryProcess.Id -Force; throw 'Isolated memory proof timed out; retained its log.' }
    }
    if ($memoryProcess.ExitCode -ne 0) { throw 'Unity memory proof failed; inspect its retained log.' }
    $memoryReport = Get-Content -LiteralPath (Join-Path $memoryRun 'export/unity-memory-validation.json') -Raw | ConvertFrom-Json
    if ($memoryReport.status -ne 'PASS' -or $memoryReport.sourceCommit -ne $memorySource) { throw 'Matching successful Unity proof required.' }
    Write-Output ('UNITY_MEMORY_EVIDENCE: ' + $memoryRun)
    Write-Output ('UNITY_MEMORY_CHECKS: ' + $memoryReport.checks.Count)
} finally {
    foreach ($memoryPath in $memorySettings.Keys) { [IO.File]::WriteAllBytes($memoryPath, $memorySettings[$memoryPath]) }
    Pop-Location
}
