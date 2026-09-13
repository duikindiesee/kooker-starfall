[CmdletBinding()]param([string]$Round='round-081')
$ErrorActionPreference='Stop'
$refugeRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(@(Get-Process Unity -ErrorAction SilentlyContinue).Count){throw 'Unity slot occupied; do not interrupt other workstreams.'}
$head=(& git -C $refugeRoot rev-parse HEAD).Trim()
if(@(& git -C $refugeRoot status --porcelain -- Assets Packages ProjectSettings tools).Count){throw 'Commit source before compiling.'}
$stamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$logs=Join-Path $refugeRoot 'evidence/local';New-Item -ItemType Directory -Force $logs | Out-Null
$log=Join-Path $logs "refuge-build-$stamp.log"
$argsList=@('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$refugeRoot+'"'),'-executeMethod','CityLife.World.Editor.RefugeBuild.Run','-starfallRefuge','-coastalPlayer','1','-kokerboomRound',$Round,'-kokerboomSeed','4242','-kokerboomWidth','1280','-previewSourceCommit',$head,'-ph02FoliageTint','1','-logFile',('"'+$log+'"'))
$p=Start-Process 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe' -ArgumentList $argsList -WindowStyle Hidden -PassThru
[pscustomobject]@{pid=$p.Id;source=$head;log=$log;round=$Round} | ConvertTo-Json
