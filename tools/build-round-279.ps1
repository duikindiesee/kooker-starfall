param(
    [string]$Editor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe',
    [string]$Round='round-279'
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
Write-Output "Starting $Round standalone player compilation..."

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
        Get-Content -LiteralPath $evidencePath -Raw | Out-Host
    }
} finally {
    if (@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { $_.CommandLine -like ('*'+$candidateRoot+'*') }).Count -eq 0) {
        foreach ($relative in $candidateBefore.Keys) { [IO.File]::WriteAllBytes((Join-Path $candidateRoot $relative),$candidateBefore[$relative]) }
    }
}
