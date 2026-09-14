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
$content = & (Join-Path $PSScriptRoot 'get-integrated-content-hash.ps1') -BuildDirectory (Split-Path $actual -Parent)
if ($launch.buildContentSchema -ne $content.schema -or $launch.buildContentSha256 -ne $content.sha256) {
    throw 'Missing or mismatched full build content fingerprint; executable-only identity is insufficient.'
}
if ($launch.buildContentSha256AfterRun -ne $launch.buildContentSha256) {
    throw 'Missing or mismatched post-run content fingerprint; tested build stability is unproven.'
}
if ($runtime.status -ne 'PASS_AUTOMATED_NATIVE_AND_COVERAGE_REVIEW_PENDING') { throw 'Expected passing automated candidate receipt.' }
$requiredChecks = @(
    'world-binding', 'spacious-finite-canyon-world', 'clothing-attached',
    'corrected-club-source-bound', 'corrected-club-uneven-terrain-clearance',
    'food-model-and-world-targets', 'berry-bush-terrain-and-rock-clearance',
    'berry-bush-camera-line-of-sight', 'forage-decoration-does-not-block-navigation',
    'moving-shallow-bed-caustics', 'actual-coastal-scene-reflection-contribution',
    'inaccessible-offshore-landforms-present',
    'complete-three-object-autonomy-cycle', 'remembered-action-receipts',
    'possessed-body-traversal', 'possessed-captured-look',
    'pause-releases-and-stops-simulation', 'pointer-opens-controls-menu',
    'pointer-returns-to-options', 'resume-restores-capture',
    'fullscreen-transition', 'window-restoration', 'free-spectator-traversal',
    'spectator-captured-look', 'spectator-escape-release',
    'refuge-approach-route', 'refuge-continuous-actor-entry-and-shelter',
    'refuge-discoverable', 'living-memory-real-receipts',
    'living-memory-persist-retrieve', 'living-memory-one-model-request',
    'living-memory-genuine-thought-required', 'no-runtime-errors'
)
foreach ($name in $requiredChecks) {
    $matches = @($runtime.checks | Where-Object { $_.name -eq $name })
    if ($matches.Count -ne 1 -or $matches[0].passed -ne $true) { throw "Missing or failed check: $name" }
}
if (@($runtime.checks | Where-Object { $_.passed -ne $true }).Count -gt 0) { throw 'Runtime contains failed checks.' }
[pscustomobject]@{
    status='IDENTITY_AND_AUTOMATED_RECEIPTS_MATCH_NOT_RELEASE_ACCEPTANCE'
    build=$manifest.buildId
    source=$manifest.sourceCommit
    executableSha256=$hash
    buildContentSha256=$content.sha256
    remaining='Visual and native review, club validation, protected review, packaging and walkthrough remain separate.'
} | ConvertTo-Json
