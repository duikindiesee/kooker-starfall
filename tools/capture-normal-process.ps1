param(
    [Parameter(Mandatory=$true)][int]$PlayerProcessId,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
$target=[IO.Path]::GetFullPath($OutputPath)
if(Test-Path -LiteralPath $target){throw 'Choose a new receipt path; evidence must not be overwritten.'}
$observed=Get-CimInstance Win32_Process -Filter ('ProcessId='+$PlayerProcessId)
if(-not $observed){throw 'Player is no longer live; launch provenance cannot be inferred.'}
if([IO.Path]::GetFileName($observed.ExecutablePath) -ne 'KookerStarfallIntegrated.exe'){throw 'Expected the integrated Starfall player.'}
$flags=[ordered]@{}
foreach($flag in @('npcLivingMemoryRuntime','integratedSmoke','npcSmoke','npcRealProbe','npcLivingMemoryEvidence')){
    $flags[$flag]=[bool]($observed.CommandLine -match ('(?i)(?:^|\s)-'+$flag+'(?:\s|$)'))
}
$receipt=[ordered]@{
    schema='starfall.normal-process-observation.v1'
    observedUtc=[DateTime]::UtcNow.ToString('o')
    processId=$PlayerProcessId
    createdUtc=$observed.CreationDate.ToUniversalTime().ToString('o')
    build=[IO.Path]::GetFileName([IO.Path]::GetDirectoryName($observed.ExecutablePath))
    executableSha256=(Get-FileHash -LiteralPath $observed.ExecutablePath -Algorithm SHA256).Hash
    flags=$flags
    # npcLivingMemoryEvidence is a passive output directory in the ordinary
    # runtime, not a scripted scenario switch. Keep it recorded, not rejected.
    normalFlagsVerified=($flags.npcLivingMemoryRuntime -and -not($flags.integratedSmoke -or $flags.npcSmoke -or $flags.npcRealProbe))
    boundary='Live OS process observation only; does not prove gameplay, model origin or persistence. Raw arguments, capabilities and private paths are excluded.'
}
$json=$receipt|ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($target,$json,[Text.UTF8Encoding]::new($false))
$json
