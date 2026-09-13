param(
    [string]$Editor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe',
    [ValidatePattern('^round-[0-9]{2,3}$')][string]$Round='round-03'
)
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if(@(Get-Process Unity -ErrorAction SilentlyContinue).Count){throw 'Coordinate with the active Unity operation before building the separate coastal player.'}
$output=Join-Path $project ('evidence\milestones\coastal\'+$Round)
if((Test-Path -LiteralPath $output)-and @(Get-ChildItem -LiteralPath $output -File).Count){throw 'Existing coastal evidence is preserved. Choose an unused round.'}
$log=Join-Path $project ('evidence\local\coastal-build-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.log')
$commit=(git -C $project rev-parse HEAD).Trim()
$args=@('-batchmode','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','CityLife.World.Editor.KokerboomRender.BuildCoastalPlayableSlice','-kokerboomRound',$Round,'-kokerboomSeed','4242','-kokerboomWidth','1600','-ph02FoliageTint','1','-previewSourceCommit',$commit,'-logFile',('"'+$log+'"'))
$p=Start-Process -FilePath $Editor -ArgumentList $args -WindowStyle Hidden -PassThru;$p.WaitForExit()
if($p.ExitCode-ne 0){throw "Coastal build failed; inspect $log"}
$evidence=Get-Content -Raw -LiteralPath (Join-Path $output 'preview-build.json')|ConvertFrom-Json
[pscustomobject]@{status=$evidence.status;player=(Join-Path $project $evidence.output);evidence=$output;log=$log}|ConvertTo-Json
