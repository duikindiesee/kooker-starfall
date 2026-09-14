param(
    [ValidatePattern('^round-[0-9]{2,3}$')][string]$Round='round-01',
    [ValidateRange(800,2560)][int]$Width=1600,
    [ValidateRange(60,1800)][int]$TimeoutSeconds=900
)
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$editor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe'
if(@(Get-Process Unity -ErrorAction SilentlyContinue).Count){throw 'Coordinate with the active Unity operation before rendering the separate coastal study.'}
$output=Join-Path $project ('evidence/milestones/coastal/'+$Round)
if((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -File).Count){throw 'Existing coastal evidence is preserved. Choose an unused round.'}
$logs=Join-Path $project 'evidence/local'
$null=New-Item -ItemType Directory -Force -Path $logs
$log=Join-Path $logs ('coastal-'+$Round+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.log')
$arguments=@('-batchmode','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','CityLife.World.Editor.KokerboomRender.RenderCoastalSlice','-kokerboomRound',$Round,'-kokerboomSeed','4242','-kokerboomWidth',$Width.ToString(),'-ph02FoliageTint','1','-logFile',('"'+$log+'"'))
$child=Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
if(-not $child.WaitForExit($TimeoutSeconds*1000)){Stop-Process -Id $child.Id -Force;throw 'Only this coastal render process exceeded its bound.'}
$child.Refresh()
if($child.ExitCode -ne 0){throw "Coastal render failed; preserve evidence and inspect $log"}
$metrics=Get-Content -LiteralPath (Join-Path $output 'metrics.json') -Raw | ConvertFrom-Json
if(-not $metrics.technicalChecksPassed -or $metrics.mode -ne 'starfall-coastal-slice-first-composition' -or @($metrics.captures).Count -ne 6){throw 'Six technically valid coastal frames, including the matched shallow-bed diagnostic pair, were not produced.'}
[pscustomobject]@{status='Six actual Unity coastal views, including a matched bed-only/water-on diagnostic pair; visual review pending';evidence=$output;log=$log;reference='Provisional user concepts; no exact match claim'} | ConvertTo-Json
