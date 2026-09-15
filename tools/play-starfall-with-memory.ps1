param(
    [Parameter(Mandatory=$true)][string]$Player,
    [string]$Storage=(Join-Path $env:LOCALAPPDATA 'Kooker\Starfall\LivingMemory'),
    [string]$WorldId='starfall.integrated-coastal.v1',
    [string]$InhabitantId='inhabitant-01',
    [string]$ModelEndpoint,
    [string]$Model,
    [string]$SurvivalModel,
    [string]$Evidence,
    [switch]$Survival,
    [string]$SurvivalEvidence,
    [switch]$DeathDiagnostic,
    [string]$DeathEvidence,
    [string]$Python
)
$ErrorActionPreference='Stop'
$playRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$playExe=(Resolve-Path -LiteralPath $Player).Path
$playBuild=(Split-Path (Split-Path $playExe -Parent) -Leaf)
if($WorldId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,95}$' -or
   $InhabitantId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$'){
    throw 'World and inhabitant IDs must be bounded path-safe identifiers.'
}
$playStorage=[IO.Path]::GetFullPath($Storage)
$playPrivate=Join-Path $playStorage 'private'
$playConfig=Join-Path $playPrivate 'config.json'
if($Python){$playPython=(Resolve-Path -LiteralPath $Python).Path}
else{
    $playBundledPython='C:\Users\irwin\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    $playPython=if(Test-Path -LiteralPath $playBundledPython){$playBundledPython}else{(Get-Command python -ErrorAction Stop).Source}
}
if(-not(Test-Path -LiteralPath $playConfig)){
    if(Test-Path -LiteralPath $playPrivate){throw 'Private memory directory exists without a valid config; choose a new storage path or migrate it explicitly.'}
    & $playPython (Join-Path $playRoot 'services/starfall-memory/local.py') init --directory $playPrivate --world-id $WorldId --build-id $playBuild --inhabitant $InhabitantId | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Private scoped memory configuration failed.'}
}
$playServiceConfig=Get-Content -LiteralPath $playConfig -Raw | ConvertFrom-Json
if($Survival -and (-not $ModelEndpoint -or -not $Model -or -not $SurvivalModel -or -not $SurvivalEvidence)){
    throw 'Survival normal play requires a local endpoint, memory model, separate survival model and empty evidence directory.'
}
if($DeathDiagnostic -and (-not $Survival -or -not $DeathEvidence)){
    throw 'Compiled death diagnostic requires survival opt-in and a separate empty evidence directory.'
}
if($Survival){
    $playModelUri=[Uri]$ModelEndpoint
    if($playModelUri.Scheme -ne 'http' -or $playModelUri.Host -notin @('127.0.0.1','localhost') -or $playModelUri.AbsolutePath -ne '/'){
        throw 'Survival model endpoint must be loopback HTTP.'
    }
    $playInventory=Invoke-RestMethod -Uri ([Uri]::new($playModelUri,'api/v1/models')) -TimeoutSec 3
    if(@($playInventory.models|Where-Object {$_.key -eq $SurvivalModel -and $_.loaded_instances.Count -eq 1}).Count -ne 1){
        throw 'Exactly one already-loaded survival model instance is required; auto-loading is not accepted.'
    }
}
if($playServiceConfig.world_id -ne $WorldId -or $playServiceConfig.publisher_id -ne 'unity-local' -or $playServiceConfig.build_ids -notcontains $playBuild -or
    @($playServiceConfig.inhabitants|Where-Object inhabitant_id -eq $InhabitantId).Count -ne 1){
    throw 'Existing private memory configuration does not match this world, inhabitant and build; migrate explicitly or choose a new storage path.'
}
function Assert-PrivateMemoryAcl([string]$Path,[bool]$RequireProtected){
    $acl=Get-Acl -LiteralPath $Path
    if($RequireProtected -and -not $acl.AreAccessRulesProtected){throw ('Private memory ACL inherits broader permissions: '+$Path)}
    $allowed=@([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18')
    foreach($rule in $acl.Access){
        if($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow){continue}
        try{$sid=$rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value}catch{throw ('Unverifiable private memory ACL identity: '+$rule.IdentityReference)}
        if($allowed -notcontains $sid){throw ('Private memory ACL grants an unexpected identity: '+$sid)}
    }
}
Assert-PrivateMemoryAcl $playPrivate $true
$playReader=($playServiceConfig.inhabitants|Where-Object inhabitant_id -eq $InhabitantId).token
$playListener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$playListener.Start();$playPort=([Net.IPEndPoint]$playListener.LocalEndpoint).Port;$playListener.Stop()
$playClient=Join-Path $playPrivate ('player-'+$playBuild+'.json')
$playClientJson=[ordered]@{world_id=$WorldId;inhabitant_id=$InhabitantId;publisher_id='unity-local';build_id=$playBuild;memory_endpoint=('http://127.0.0.1:'+$playPort);publisher_token=$playServiceConfig.publisher_token;reader_token=$playReader}|ConvertTo-Json -Compress
[IO.File]::WriteAllText($playClient,$playClientJson,[Text.UTF8Encoding]::new($false))
$playLegacyData=Join-Path $playStorage 'data'
$playData=Join-Path $playPrivate 'data'
if((Test-Path -LiteralPath (Join-Path $playLegacyData 'starfall-memory.sqlite3')) -and -not(Test-Path -LiteralPath (Join-Path $playData 'starfall-memory.sqlite3'))){
    throw 'Memory database predates private placement; migrate it explicitly into the protected private/data directory before launch.'
}
$null=New-Item -ItemType Directory -Force -Path $playData
Assert-PrivateMemoryAcl $playData $false
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
        if($Evidence){
            $playEvidence=[IO.Path]::GetFullPath($Evidence)
            if(Test-Path -LiteralPath $playEvidence){
                if((Get-ChildItem -LiteralPath $playEvidence -Force|Select-Object -First 1)){throw 'Normal-play evidence directory must be empty.'}
            }else{$null=New-Item -ItemType Directory -Path $playEvidence}
            $playArgs+=@('-npcLivingMemoryEvidence',$playEvidence)
        }
        if($ModelEndpoint -and $Model){$playArgs+=@('-npcLocalEndpoint',$ModelEndpoint,'-npcLocalModel',$Model)}
        elseif($ModelEndpoint -or $Model){Write-Warning 'Both ModelEndpoint and Model are required for optional thought; action memory will continue without model calls.'}
        if($Survival){
            $playSurvivalData=Join-Path $playData 'survival-food'
            $null=New-Item -ItemType Directory -Force -Path $playSurvivalData
            Assert-PrivateMemoryAcl $playSurvivalData $false
            $playSurvivalSave=Join-Path $playSurvivalData ($WorldId+'-'+$InhabitantId+'.json')
            $playSurvivalEvidence=[IO.Path]::GetFullPath($SurvivalEvidence)
            if(Test-Path -LiteralPath $playSurvivalEvidence){
                if((Get-ChildItem -LiteralPath $playSurvivalEvidence -Force|Select-Object -First 1)){throw 'Survival evidence directory must be empty.'}
            }else{$null=New-Item -ItemType Directory -Path $playSurvivalEvidence}
            $playArgs+=@('-npcSurvivalRuntime','-npcSurvivalModel',$SurvivalModel,
                '-npcSurvivalSave',$playSurvivalSave,'-npcSurvivalEvidence',$playSurvivalEvidence)
            if($DeathDiagnostic){
                $playDeathEvidence=[IO.Path]::GetFullPath($DeathEvidence)
                if(Test-Path -LiteralPath $playDeathEvidence){
                    if((Get-ChildItem -LiteralPath $playDeathEvidence -Force|Select-Object -First 1)){throw 'Death diagnostic evidence directory must be empty.'}
                }else{$null=New-Item -ItemType Directory -Path $playDeathEvidence}
                $playArgs+=@('-npcSurvivalDeathAcceptance','-npcSurvivalDeathEvidence',$playDeathEvidence)
            }
        }
    }else{
        if($Survival){throw 'Survival launch requires the scoped living-memory service to be ready.'}
        Write-Warning 'Living-memory service was unavailable; launching deterministic gameplay without memory integration.'
    }
    $playGameArgs=($playArgs|ForEach-Object{if($_ -match '\s'){'"'+$_+'"'}else{$_}})-join ' '
    $playGame=Start-Process -FilePath $playExe -ArgumentList $playGameArgs -WorkingDirectory (Split-Path $playExe -Parent) -WindowStyle Normal -PassThru
    $playGame.WaitForExit()
    exit $playGame.ExitCode
}finally{
    if($playService -and -not $playService.HasExited){$playService.Kill();$playService.WaitForExit()}
}
