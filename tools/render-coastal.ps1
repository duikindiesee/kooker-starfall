param(
    [ValidatePattern('^round-[0-9]{2,3}$')][string]$Round='round-01',
    [ValidateRange(800,2560)][int]$Width=1600,
    [ValidateRange(60,1800)][int]$TimeoutSeconds=900,
    [switch]$SkySites,
    [switch]$FilmSites
)
$ErrorActionPreference='Stop'
if($SkySites -and $FilmSites){throw 'Choose sky or film site diagnostic, not both.'}
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$editor='C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe'
if(@(Get-Process Unity -ErrorAction SilentlyContinue).Count){throw 'Coordinate with the active Unity operation before rendering the separate coastal study.'}
$output=Join-Path $project ('evidence/milestones/coastal/'+$Round)
if((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -File).Count){throw 'Existing coastal evidence is preserved. Choose an unused round.'}
$logs=Join-Path $project 'evidence/local'
$null=New-Item -ItemType Directory -Force -Path $logs
$log=Join-Path $logs ('coastal-'+$Round+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.log')
$method=if($SkySites){'CityLife.World.Editor.KokerboomRender.RenderCoastalSkySites'}elseif($FilmSites){'CityLife.World.Editor.KokerboomRender.RenderCoastalFilmSites'}else{'CityLife.World.Editor.KokerboomRender.RenderCoastalSlice'}
$arguments=@('-batchmode','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod',$method,'-kokerboomRound',$Round,'-kokerboomSeed','4242','-kokerboomWidth',$Width.ToString(),'-ph02FoliageTint','1','-logFile',('"'+$log+'"'))
$child=Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
if(-not $child.WaitForExit($TimeoutSeconds*1000)){Stop-Process -Id $child.Id -Force;throw 'Only this coastal render process exceeded its bound.'}
$child.Refresh()
if($child.ExitCode -ne 0){throw "Coastal render failed; preserve evidence and inspect $log"}
$metrics=Get-Content -LiteralPath (Join-Path $output 'metrics.json') -Raw | ConvertFrom-Json
$expected=if($SkySites){4}elseif($FilmSites){3}else{7}
if(-not $metrics.technicalChecksPassed -or $metrics.mode -ne 'starfall-coastal-slice-first-composition' -or @($metrics.captures).Count -ne $expected){throw "$expected technically valid coastal frames were not produced; preserve partial evidence."}
if($SkySites -and -not(Test-Path -LiteralPath (Join-Path $output 'sky-site-selection.txt'))){throw 'Matched sky site selection receipt missing.'}
if($FilmSites -and -not(Test-Path -LiteralPath (Join-Path $output 'film-site-selection.txt'))){throw 'Matched film site selection receipt missing.'}
[pscustomobject]@{status=if($SkySites){'Four matched-terrain dry sky site diagnostic views; not playable camera acceptance'}elseif($FilmSites){'Three actor-aimed film camera diagnostics; actor is a proxy location, not playable footage'}else{'Seven actual Unity coastal views, including matched bed-only/water-on and offshore-island evidence; visual review pending'};evidence=$output;log=$log;reference='User-approved references; no exact match claim'} | ConvertTo-Json
