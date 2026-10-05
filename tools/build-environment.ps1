param([string]$Editor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$existing=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($taskRoot) }
if($existing){throw 'An editor already owns this environment worktree. Wait for it to finish.'}
$stamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$log=Join-Path $taskRoot "evidence\local\build-$stamp.log"
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
$backup=@{}
$paths=git -C $taskRoot ls-files ProjectSettings Assets/Settings Assets/CityLife/Scenes/Island.unity
foreach($relative in $paths){$path=Join-Path $taskRoot $relative;$backup[$path]=[IO.File]::ReadAllBytes($path)}
try {
    $arguments=@('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$taskRoot+'"'),'-executeMethod','Starfall.EnvironmentFoundation.Editor.EnvironmentBuild.Run','-logFile',('"'+$log+'"'))
    $process=Start-Process -FilePath $Editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if($process.ExitCode -ne 0){throw "Environment build exited $($process.ExitCode); see $log"}
    Get-Content (Join-Path $taskRoot 'evidence/local/environment-build.json')
} finally {
    foreach($path in $backup.Keys){[IO.File]::WriteAllBytes($path,$backup[$path])}
}
