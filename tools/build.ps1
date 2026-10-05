param(
    [string]$Editor = 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe',
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$BuildName = 'Windows'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not (Test-Path -LiteralPath $Editor)) { throw "Unity 6000.6.0f1 editor not found at $Editor" }
$evidenceFolder = Join-Path $projectRoot 'evidence\local'
New-Item -ItemType Directory -Force -Path $evidenceFolder | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$log = Join-Path $evidenceFolder "build-$stamp.log"
$buildArguments = @('-batchmode', '-quit', '-projectPath', ('"' + $projectRoot + '"'), '-buildTarget', 'StandaloneWindows64', '-executeMethod', 'CityLife.World.Editor.CityLifeBuild.BuildWindows', '-citylifeBuildName', $BuildName, '-logFile', ('"' + $log + '"'))
$buildProcess = Start-Process -FilePath $Editor -ArgumentList $buildArguments -WindowStyle Hidden -PassThru
$buildProcess.WaitForExit()
if ($buildProcess.ExitCode -ne 0) { throw "Build failed with exit code $($buildProcess.ExitCode). Inspect $log" }
Write-Output "Windows player: $(Join-Path $projectRoot ('Builds\' + $BuildName + '\CityLife.exe'))"
Write-Output "Build evidence: $log"
