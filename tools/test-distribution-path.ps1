$ErrorActionPreference = 'Stop'
$guard = Join-Path $PSScriptRoot 'check-distribution-path.ps1'
$accepted = @('Game.exe','UnityPlayer.dll','Game_Data/globalgamemanagers','Game_Data/StreamingAssets/world.json','MonoBleedingEdge/etc/mono/config','Game_Data/Managed/System.Security.dll')
$rejected = @('cache.sqlite3','Game_Data/.ENV.production','key.PFX','x/client.p12','identity.ulf','x/key.keystore','x/data.db3','x/events.jsonl','x/crash.dmp','x/old.zip','x/Worlds/save.json','x/SESSIONS/state.json','x/.git/config','x/Memory/store.json','x/BackUpThisFolder_ButDontShipItWithYourGame/source.cs','x/credentials.json','../escape','/absolute','C:/absolute','x//y','x/./y','x/../y','x/trailing.','x/trailing ')
foreach ($path in $accepted) { & $guard -RelativePath $path }
foreach ($path in $rejected) {
    $failed = $false
    try { & $guard -RelativePath $path } catch { $failed = $true }
    if (!$failed) { throw "Private/unsafe synthetic path accepted: $path" }
}
[ordered]@{status='SYNTHETIC_DISTRIBUTION_PATH_TESTS_PASS';accepted=$accepted.Count;rejected=$rejected.Count;boundary='Pure path checks only; no real package created or certified.'} | ConvertTo-Json
