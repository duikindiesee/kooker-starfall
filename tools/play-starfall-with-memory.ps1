param(
    [Parameter(Mandatory=$true)][string]$Player,
    [string]$Storage=(Join-Path $env:LOCALAPPDATA 'Kooker\Starfall\LivingMemory'),
    [string]$WorldId='starfall.integrated-coastal.v1',
    [string]$InhabitantId='inhabitant-01',
    [string]$ModelEndpoint,
    [string]$Model
)
$ErrorActionPreference='Stop'
$playRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$playExe=(Resolve-Path -LiteralPath $Player).Path
$playBuild=(Split-Path (Split-Path $playExe -Parent) -Leaf)
$playStorage=[IO.Path]::GetFullPath($Storage)
$playPrivate=Join-Path $playStorage 'private'
$playConfig=Join-Path $playPrivate 'config.json'
$playPython=(Get-Command python -ErrorAction Stop).Source
if(-not(Test-Path -LiteralPath $playConfig)){
    if(Test-Path -LiteralPath $playPrivate){throw 'Private memory directory exists without a valid config; choose a new storage path or migrate it explicitly.'}
    & $playPython (Join-Path $playRoot 'services/starfall-memory/local.py') init --directory $playPrivate --world-id $WorldId --build-id $playBuild --inhabitant $InhabitantId | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Private scoped memory configuration failed.'}
}
$playServiceConfig=Get-Content -LiteralPath $playConfig -Raw | ConvertFrom-Json
if($playServiceConfig.world_id -ne $WorldId -or $playServiceConfig.publisher_id -ne 'unity-local' -or $playServiceConfig.build_ids -notcontains $playBuild -or
    @($playServiceConfig.inhabitants|Where-Object inhabitant_id -eq $InhabitantId).Count -ne 1){
    throw 'Existing private memory configuration does not match this world, inhabitant and build; migrate explicitly or choose a new storage path.'
}
$playReader=($playServiceConfig.inhabitants|Where-Object inhabitant_id -eq $InhabitantId).token
$playListener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$playListener.Start();$playPort=([Net.IPEndPoint]$playListener.LocalEndpoint).Port;$playListener.Stop()
$playClient=Join-Path $playPrivate ('player-'+$playBuild+'.json')
$playClientJson=[ordered]@{world_id=$WorldId;inhabitant_id=$InhabitantId;publisher_id='unity-local';build_id=$playBuild;memory_endpoint=('http://127.0.0.1:'+$playPort);publisher_token=$playServiceConfig.publisher_token;reader_token=$playReader}|ConvertTo-Json -Compress
[IO.File]::WriteAllText($playClient,$playClientJson,[Text.UTF8Encoding]::new($false))
$playData=Join-Path $playStorage 'data';$null=New-Item -ItemType Directory -Force -Path $playData
$playLog=Join-Path $playStorage ('service-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.log')
$playServiceArgs='"'+(Join-Path $playRoot 'services/starfall-memory/offline_guard.py')+'" --config "'+$playConfig+'" --data-dir "'+$playData+'" --port '+$playPort
$playService=Start-Process -FilePath $playPython -ArgumentList $playServiceArgs -RedirectStandardOutput $playLog -RedirectStandardError ($playLog+'.err') -WindowStyle Hidden -PassThru
$playReady=$false
try{
    for($i=0;$i -lt 40;$i++){
        if($playService.HasExited){break}
        try{$null=Invoke-RestMethod -Uri ('http://127.0.0.1:'+$playPort+'/v1/health') -Headers @{Authorization=('Bearer '+$playReader)} -TimeoutSec 1;$playReady=$true;break}catch{Start-Sleep -Milliseconds 100}
    }
    $playArgs=@('-force-d3d11')
    if($playReady){
        $playOutbox=Join-Path $playPrivate 'outbox'
        $playArgs+=@('-npcLivingMemoryRuntime','-npcMemoryClient',$playClient,'-npcMemoryOutbox',$playOutbox,'-npcMemoryBuild',$playBuild)
        if($ModelEndpoint -and $Model){$playArgs+=@('-npcLocalEndpoint',$ModelEndpoint,'-npcLocalModel',$Model)}
        elseif($ModelEndpoint -or $Model){Write-Warning 'Both ModelEndpoint and Model are required for optional thought; action memory will continue without model calls.'}
    }else{Write-Warning 'Living-memory service was unavailable; launching deterministic gameplay without memory integration.'}
    $playGameArgs=($playArgs|ForEach-Object{if($_ -match '\s'){'"'+$_+'"'}else{$_}})-join ' '
    $playGame=Start-Process -FilePath $playExe -ArgumentList $playGameArgs -WorkingDirectory (Split-Path $playExe -Parent) -WindowStyle Normal -PassThru
    $playGame.WaitForExit()
    exit $playGame.ExitCode
}finally{
    if($playService -and -not $playService.HasExited){$playService.Kill();$playService.WaitForExit()}
}
