param(
    [Parameter(Mandatory)][ValidatePattern('^KookerStarfallR19-0\.0\.2-preview\.2-[0-9]{8}-[0-9]{6}$')][string]$BuildName,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommit,
    [string]$PythonExecutable = 'python'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$buildsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Builds'))
$playerFolder = [IO.Path]::GetFullPath((Join-Path $buildsRoot $BuildName))
if ($BuildName.Contains('..') -or -not $playerFolder.StartsWith($buildsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build must remain inside this project Builds directory.' }
foreach ($folder in @($buildsRoot, $playerFolder)) {
    if (-not (Test-Path -LiteralPath $folder -PathType Container) -or ((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Build directories must exist and cannot be links.' }
}
$playerExecutable = 'KookerStarfallR19.exe'
foreach ($required in @($playerExecutable, 'KookerStarfallR19_Data/globalgamemanagers', 'UnityPlayer.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $playerFolder $required) -PathType Leaf)) { throw 'Build the separate R19 Windows player first; required player files are missing.' }
}
# The caller supplies the actual build source commit. Confirm it is a commit object;
# this records provenance without claiming that packaging reproduces the build.
$commitType = & git -C $projectRoot cat-file -t $SourceCommit
if ($LASTEXITCODE -ne 0 -or $commitType -ne 'commit') { throw 'SourceCommit must identify an existing exact source commit.' }
$SourceCommit = $SourceCommit.ToLowerInvariant()
$treeBasisCommit = 'fc30b2857be419172e740f0d338d5913145d75fb'
$releaseVersion = '0.0.2-preview.2'
$releaseFolder = Join-Path $buildsRoot 'Download'
if (Test-Path -LiteralPath $releaseFolder) {
    if (-not (Test-Path -LiteralPath $releaseFolder -PathType Container) -or ((Get-Item -LiteralPath $releaseFolder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Download must be a real directory.' }
} else { $null = New-Item -ItemType Directory -Path $releaseFolder }
$archiveName = $BuildName + '-Windows.zip'
$zip = Join-Path $releaseFolder $archiveName
foreach ($destination in @($zip, $zip + '.sha256', $zip + '.manifest.json')) {
    if (Test-Path -LiteralPath $destination) { throw 'Package destination already exists; refusing to overwrite any release artifact.' }
}
$noticePath = Join-Path $projectRoot 'Assets/CityLife/Art/THIRD-PARTY-NOTICES.txt'
$cc0Path = Join-Path $projectRoot 'Assets/CityLife/Art/Licenses/CC0-1.0.txt'
if (-not (Test-Path -LiteralPath $noticePath -PathType Leaf) -or -not (Test-Path -LiteralPath $cc0Path -PathType Leaf)) { throw 'Complete art notices and CC0 legal text are required.' }
$utf8 = [Text.UTF8Encoding]::new($false)
$readme = @"
KOOKER: STARFALL - SEPARATE R19 PLAYER
Release version: $releaseVersion
Build identifier: $BuildName
Build source commit (provided by builder): $SourceCommit
Frozen tree basis: $treeBasisCommit

Extract this whole archive into a new folder and open KookerStarfallR19.exe.
Keep KookerStarfallR19_Data, MonoBleedingEdge and DLLs beside the executable.
This distinct Windows preview does not replace or modify the R06 reference.

CONTROLS
WASD moves. Hold right mouse to look; release it or press Escape to release
the pointer. Shift moves faster. F switches walk/fly. In fly mode Q/E moves
down/up. F12 capture and local input/evidence recording require an explicit
absolute -previewEvidence folder supplied when launching the player.

RELEASE NOTES - $releaseVersion
Adds a visible fullscreen/windowed button and Alt+Enter shortcut. Restores
the previous usable window dimensions without reloading the scene. The
window can be resized. Earlier preview.1 and R06 artifacts are preserved.

Separate playable build of the frozen R19 PH02 tree experiment: native-scale
source crowns, fitted support, shared branch skin, blue-green foliage and
revised ground contact. R19 is a review label, not the semantic version.
The earlier R06 CosmicWorldPreview.exe remains the user-approved visual
reference and is distributed separately with unchanged runtime bytes.
The final R19 critic score is 7.375/10 (7.4 displayed); remaining defects are
deferred. No automatic promotion over R06 or further polishing is implied.
This is a small baked study, not the larger continuous world or living sea.

Packaging verifies archive bytes and does not launch or accept the player.
Native controls, collision, frame rate and offline/telemetry behaviour must
be judged from the corresponding actual runtime evidence, not this package.
See ASSET-CREDITS.txt and THIRD-PARTY-NOTICES.txt for source art provenance.
Source: https://github.com/duikindiesee/kooker-starfall
"@
$credits = @"
KOOKER: STARFALL - R19 DERIVATIVE ART CREDITS

Quiver Tree 02 - Poly Haven
Dario Barresi (photography); Rico Cilliers (modelling).
https://polyhaven.com/a/quiver_tree_02
PH02 source crown geometry and textures form the fitted crown derivative.

Quiver Tree 01 - Poly Haven
Dario Barresi and James Ray Cock (photography); Rico Cilliers (modelling).
https://polyhaven.com/a/quiver_tree_01
PH01 bark texture sampling contributes to the procedural wood material.

Publisher licence: https://polyhaven.com/license (CC0 1.0 Universal).
Source FBX/texture downloads are unchanged in the repository. Generated
crown clipping/fitting, branch architecture, placement and materials are
derivatives; the publisher's quiver-tree label is not species certification
or endorsement. User-supplied conceptual artwork is reference only, not a
redistributed asset. Complete repository art notices follow in the separate
THIRD-PARTY-NOTICES.txt; catalogue-only entries are labelled there and do not
assert that those preview/model assets ship in this player.
"@
$documents = [ordered]@{
    'README.txt' = $utf8.GetBytes($readme)
    'ASSET-CREDITS.txt' = $utf8.GetBytes($credits)
    'THIRD-PARTY-NOTICES.txt' = [IO.File]::ReadAllBytes($noticePath)
    'CC0-1.0.txt' = [IO.File]::ReadAllBytes($cc0Path)
}
$files = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$pending = [Collections.Generic.Queue[string]]::new()
$pending.Enqueue($playerFolder)
$excludedPaths = 0
while ($pending.Count -gt 0) {
    $directory = $pending.Dequeue()
    foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked build content cannot be packaged.' }
        $relative = $item.FullName.Substring($playerFolder.Length + 1).Replace('\', '/')
        if ($relative -match '(^/|:|(^|/)\.\.(/|$)|[\x00-\x1f])') { throw 'Unsafe build entry.' }
        # Exclusions apply to every directory depth; never traverse private trees.
        if ($item.Name -like '*BackUpThisFolder_ButDontShipItWithYourGame*' -or $item.Name -match '^(\.git|Library|UserSettings|Worlds|Sessions|Evidence|Logs)$' -or $item.Name -like '.env*' -or $item.Extension -match '^\.(ulf|alf|pem|key|p12|pfx|jks|keystore|sqlite|sqlite3|db|db3|log|dmp|zip|7z|rar)$') {
            $excludedPaths++; continue
        }
        if ($item.PSIsContainer) { $pending.Enqueue($item.FullName); continue }
        if ($documents.Contains($relative)) { throw 'Build contains a reserved portable-document name; refusing to replace it.' }
        if (-not $seen.Add($relative)) { throw 'Duplicate Windows build path.' }
        $files.Add([ordered]@{path=$relative; source=$item.FullName; bytes=$item.Length; sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()})
    }
}
$entries = [Collections.Generic.List[object]]::new()
$tempBase = Join-Path $releaseFolder ('.package-r19-' + [Guid]::NewGuid().ToString('N'))
$tempZip = $tempBase + '.zip'
$tempManifest = $tempZip + '.manifest.json'
$tempChecksum = $tempBase + '.sha256'
try {
    $zipStream = [IO.File]::Open($tempZip, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($file in $files) {
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.source, $file.path, [IO.Compression.CompressionLevel]::Optimal)
            $entries.Add([ordered]@{path=$file.path; bytes=$file.bytes; sha256=$file.sha256})
        }
        foreach ($name in $documents.Keys) {
            $bytes = $documents[$name]
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $stream = $entry.Open()
            try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($hasher.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() } finally { $hasher.Dispose() }
            $entries.Add([ordered]@{path=$name; bytes=$bytes.Length; sha256=$hash})
        }
    } finally { $archive.Dispose(); $zipStream.Dispose() }
    foreach ($file in $files) {
        if ((Get-FileHash -LiteralPath $file.source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw 'Build changed during packaging.' }
    }
    $checksum = (Get-FileHash -LiteralPath $tempZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{schema='citylife.unity.package-evidence.v1'; utc=[DateTime]::UtcNow.ToString('o'); archive=[IO.Path]::GetFileName($tempZip); archiveBytes=(Get-Item -LiteralPath $tempZip).Length; sha256=$checksum; buildDirectory=('Builds/' + $BuildName); playerExecutable=$playerExecutable; releaseVersion=$releaseVersion; sourceCommit=$SourceCommit; frozenTreeBasisCommit=$treeBasisCommit; sourceCommitBasis='Explicit builder-supplied commit; packaging does not reproduce compilation'; preservedReference='R06 retained separately; no reads or writes to its build/archive'; unchangedRuntimeFileCount=$files.Count; addedDocumentCount=$documents.Count; excludedPathCount=$excludedPaths; verifiedEntryCount=$entries.Count; entries=$entries}
    [IO.File]::WriteAllText($tempManifest, ($manifest | ConvertTo-Json -Depth 6), $utf8)
    & $PythonExecutable (Join-Path $PSScriptRoot 'check-package.py') --archive $tempZip --player-executable $playerExecutable
    if ($LASTEXITCODE -ne 0) { throw 'Independent package integrity guard failed.' }
    $manifest.archive = $archiveName
    [IO.File]::WriteAllText($tempManifest, ($manifest | ConvertTo-Json -Depth 6), $utf8)
    [IO.File]::WriteAllText($tempChecksum, ($checksum + '  ' + $archiveName + [Environment]::NewLine), $utf8)
    # File.Move has no overwrite flag: even a concurrent destination creation fails closed.
    [IO.File]::Move($tempZip, $zip)
    [IO.File]::Move($tempManifest, ($zip + '.manifest.json'))
    [IO.File]::Move($tempChecksum, ($zip + '.sha256'))
    Write-Output $zip
    Write-Output "SHA256 $checksum"
    Write-Output "Verified $($entries.Count) archive files; $($files.Count) runtime files unchanged; R06 untouched."
} finally {
    # Only delete the three exact private temporary files created by this call.
    foreach ($temporary in @($tempZip, $tempManifest, $tempChecksum)) {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) { [IO.File]::Delete($temporary) }
    }
}
