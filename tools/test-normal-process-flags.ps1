$ErrorActionPreference = 'Stop'
# Exercise the expression used by the live process observer without launching a player.
$source = Get-Content (Join-Path $PSScriptRoot 'capture-normal-process.ps1') -Raw
$expression = [regex]::Match($source, '(?m)^\s*normalFlagsVerified=(.+)$').Groups[1].Value
if (-not $expression) { throw 'Normal-play classification expression missing.' }
$evaluate = [scriptblock]::Create($expression)
$cases = 0
foreach ($runtime in @($false,$true)) {
    foreach ($evidence in @($false,$true)) {
        foreach ($smoke in @($false,$true)) {
            foreach ($npcSmoke in @($false,$true)) {
                foreach ($probe in @($false,$true)) {
                    $flags = @{ npcLivingMemoryRuntime=$runtime; npcLivingMemoryEvidence=$evidence;
                        integratedSmoke=$smoke; npcSmoke=$npcSmoke; npcRealProbe=$probe }
                    $expected = $runtime -and -not ($smoke -or $npcSmoke -or $probe)
                    if ((& $evaluate) -ne $expected) { throw "Incorrect classification: $($flags | ConvertTo-Json -Compress)" }
                    $cases++
                }
            }
        }
    }
}
"PASS: $cases flag combinations. Passive evidence output does not change normal-play classification."
