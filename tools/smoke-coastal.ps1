param([Parameter(Mandatory)][string]$Player)
$ErrorActionPreference='Stop';$playerPath=(Resolve-Path -LiteralPath $Player).Path
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$dir=Join-Path $project ('evidence\local\coastal-runtime-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $dir|Out-Null
$args=@('-batchmode','-coastalSmoke','-coastalEvidence',('"'+$dir+'"'),'-logFile',('"'+(Join-Path $dir 'player.log')+'"'))
$p=Start-Process -FilePath $playerPath -ArgumentList $args -WindowStyle Hidden -PassThru
if(-not $p.WaitForExit(180000)){$p.Kill();throw 'Coastal runtime smoke exceeded 180 seconds.'}
if($p.ExitCode-ne 0){throw "Coastal runtime smoke failed; inspect $dir"}
Get-Content -Raw -LiteralPath (Join-Path $dir 'coastal-runtime-smoke.json')
