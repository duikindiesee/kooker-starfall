param(
    [Parameter(Mandatory)][string]$BuildManifest,
    [Parameter(Mandatory)][string]$RuntimeDirectory
)
$ErrorActionPreference = 'Stop'
# Read-only identity preflight. This is not release or visual acceptance.
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$manifest = Get-Content -LiteralPath $BuildManifest -Raw | ConvertFrom-Json
$launch = Get-Content -LiteralPath (Join-Path $RuntimeDirectory 'launch.json') -Raw | ConvertFrom-Json
$runtime = Get-Content -LiteralPath (Join-Path $RuntimeDirectory 'runtime/integrated-runtime.json') -Raw | ConvertFrom-Json
if ($manifest.status -ne 'Succeeded') { throw 'Build did not succeed.' }
if ($manifest.buildId -notmatch '^KookerStarfallIntegrated-[A-Za-z0-9.-]+$') { throw 'Not an integrated build identifier.' }
if ($manifest.sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Exact source commit required.' }
if ($launch.build -ne $manifest.buildId -or $launch.source_commit -ne $manifest.sourceCommit) { throw 'Runtime and build identities differ.' }
$expected = Join-Path $root ('Builds/' + $manifest.buildId + '/KookerStarfallIntegrated.exe')
$actual = [IO.Path]::GetFullPath((Join-Path $root $manifest.output))
if ($actual -ne [IO.Path]::GetFullPath($expected)) { throw 'Unexpected player location.' }
if ((Get-Item -LiteralPath $actual).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked executable rejected.' }
$hash = (Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $launch.sha256) { throw 'Player bytes differ from runtime receipt.' }
if ($runtime.status -ne 'PASS_AUTOMATED_NATIVE_AND_COVERAGE_REVIEW_PENDING') { throw 'Expected passing automated candidate receipt.' }
foreach ($name in @('complete-three-object-autonomy-cycle','remembered-action-receipts','forage-decoration-does-not-block-navigation')) {
    $matches = @($runtime.checks | Where-Object { $_.name -eq $name })
    if ($matches.Count -ne 1 -or $matches[0].passed -ne $true) { throw "Missing or failed check: $name" }
}
if (@($runtime.checks | Where-Object { $_.passed -ne $true }).Count -gt 0) { throw 'Runtime contains failed checks.' }
[pscustomobject]@{
    status='IDENTITY_AND_AUTOMATED_RECEIPTS_MATCH_NOT_RELEASE_ACCEPTANCE'
    build=$manifest.buildId
    source=$manifest.sourceCommit
    executableSha256=$hash
    remaining='Visual and native review, club validation, protected review, packaging and walkthrough remain separate.'
} | ConvertTo-Json
