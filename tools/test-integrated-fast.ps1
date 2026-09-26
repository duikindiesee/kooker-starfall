param(
    [string]$Editor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe'
)
$ErrorActionPreference='Stop'
$candidateRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (@(Get-Process Unity -ErrorAction SilentlyContinue).Count) {
    Write-Warning 'Another Unity process is active; coordinating...'
}
$candidateLogDir=Join-Path $candidateRoot 'evidence/local/validation'
$null=New-Item -ItemType Directory -Force -Path $candidateLogDir
$candidateStamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$candidateLog=Join-Path $candidateLogDir ('fast-validation-'+$candidateStamp+'.log')
$candidateSettings=@('ProjectSettings/ProjectSettings.asset','ProjectSettings/GraphicsSettings.asset','ProjectSettings/QualitySettings.asset','Assets/Settings/UniversalRenderPipelineGlobalSettings.asset')
$candidateBefore=@{}
foreach ($relative in $candidateSettings) { $path=Join-Path $candidateRoot $relative; if (Test-Path -LiteralPath $path) { $candidateBefore[$relative]=[IO.File]::ReadAllBytes($path) } }

$watch=[Diagnostics.Stopwatch]::StartNew()
Write-Output "Starting fast in-flight Unity CLI validation..."

try {
    $candidateArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$candidateRoot+'"'),'-executeMethod','CityLife.World.Editor.IntegratedCoastalBuild.RunValidationOnly','-starfallIntegrated','-logFile',('"'+$candidateLog+'"'))
    $candidateProcess=Start-Process -FilePath $Editor -ArgumentList $candidateArgs -WindowStyle Hidden -PassThru
    Write-Output ('Unity PID '+$candidateProcess.Id+' | '+$candidateLog)
    $candidateProcess.WaitForExit()
    $watch.Stop()
    if ($candidateProcess.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $candidateLog) {
            Get-Content -LiteralPath $candidateLog -Tail 50 | Out-Host
        }
        throw ('Fast validation failed with exit code '+$candidateProcess.ExitCode+': '+$candidateLog)
    }
    $logContent=Get-Content -LiteralPath $candidateLog -Raw
    if ($logContent -match 'STARFALL_INTEGRATED_VALIDATION_PASSED: (\d+) named checks') {
        $checks=$Matches[1]
        Write-Output ("FAST_VALIDATION_PASSED: $checks checks verified in "+[Math]::Round($watch.Elapsed.TotalSeconds, 1)+'s!')
    } else {
        Write-Output ("FAST_VALIDATION_PASSED in "+[Math]::Round($watch.Elapsed.TotalSeconds, 1)+'s!')
    }
} finally {
    if (@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { $_.CommandLine -like ('*'+$candidateRoot+'*') }).Count -eq 0) {
        foreach ($relative in $candidateBefore.Keys) { [IO.File]::WriteAllBytes((Join-Path $candidateRoot $relative),$candidateBefore[$relative]) }
    }
}
