param(
    [string]$Editor = 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$logs = Join-Path $project 'evidence/local'
$null = New-Item -ItemType Directory -Force -Path $logs
$log = Join-Path $logs ('catfish-render-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '.log')

$args = @('-batchmode', '-force-d3d11', '-projectPath', ('"' + $project + '"'), '-executeMethod', 'CityLife.Art.Catfish.Editor.CatfishVisualReceipt.Run', '-logFile', ('"' + $log + '"'))
Write-Output "Rendering Catfish inspection in Unity batchmode..."
$proc = Start-Process -FilePath $Editor -ArgumentList $args -WindowStyle Hidden -PassThru
$finished = $proc.WaitForExit(90000)

if (-not $finished) {
    Stop-Process -Id $proc.Id -Force
    throw "Catfish render timed out after 90s"
}

if ($proc.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $log) {
        Get-Content -LiteralPath $log -Tail 40 | Out-Host
    }
    throw "Catfish render failed with exit code $($proc.ExitCode)"
}

Write-Output "Catfish render complete! Exit code: $($proc.ExitCode)"
