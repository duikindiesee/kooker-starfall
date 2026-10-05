param([ValidatePattern('^[A-Za-z0-9_-]+$')][string]$BuildName = 'Windows')
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$playerFolder = Join-Path $projectRoot ('Builds\' + $BuildName)
if (-not (Test-Path -LiteralPath (Join-Path $playerFolder 'CityLife.exe'))) { throw 'Build the Windows player first.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\PLAYER-README.txt') -Destination (Join-Path $playerFolder 'README.txt') -Force
$noticePath = Join-Path $projectRoot 'Assets\CityLife\Art\THIRD-PARTY-NOTICES.txt'
if (Test-Path -LiteralPath $noticePath) { Copy-Item -LiteralPath $noticePath -Destination (Join-Path $playerFolder 'THIRD-PARTY-NOTICES.txt') -Force }
$releaseFolder = Join-Path $projectRoot 'Builds\Download'
New-Item -ItemType Directory -Force -Path $releaseFolder | Out-Null
$zip = Join-Path $releaseFolder 'CityLife-Island-0.1.0-Windows.zip'
# Unity explicitly excludes this generated backup folder from distribution.
$contentPaths = Get-ChildItem -LiteralPath $playerFolder | Where-Object { $_.Name -notlike '*BackUpThisFolder_ButDontShipItWithYourGame*' } | ForEach-Object FullName
$expectedFiles = @(foreach ($contentPath in $contentPaths) { if (Test-Path -LiteralPath $contentPath -PathType Container) { Get-ChildItem -LiteralPath $contentPath -Recurse -File -Force } else { Get-Item -LiteralPath $contentPath } })
Compress-Archive -LiteralPath $contentPaths -DestinationPath $zip -Force
$checksum = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($zip + '.sha256') -Value ($checksum + '  ' + (Split-Path -Leaf $zip)) -Encoding ascii
# Check archive entries against the exact distributable files without extracting or launching it.
$packageEntries = [System.Collections.Generic.List[object]]::new()
$seenEntries = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($entry in $archive.Entries) {
        if (-not $entry.Name) { continue }
        if (-not $seenEntries.Add($entry.FullName)) { throw "Duplicate archive entry: $($entry.FullName)" }
        if ($entry.FullName -match '(^[\\/]|^[A-Za-z]:|(^|[\\/])\.\.([\\/]|$)|BackUpThisFolder_ButDontShipItWithYourGame)') { throw "Unsafe archive entry: $($entry.FullName)" }
        $originalFile = Join-Path $playerFolder $entry.FullName
        if (-not (Test-Path -LiteralPath $originalFile -PathType Leaf)) { throw "Unexpected archive entry: $($entry.FullName)" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $entryHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($entryHash -ne (Get-FileHash -LiteralPath $originalFile -Algorithm SHA256).Hash.ToLowerInvariant()) { throw "Archive content differs: $($entry.FullName)" }
        $packageEntries.Add([ordered]@{path=$entry.FullName.Replace('\','/'); bytes=$entry.Length; sha256=$entryHash})
    }
}
finally { $archive.Dispose() }
if ($packageEntries.Count -ne $expectedFiles.Count) { throw 'Archive omitted one or more distributable files.' }
$manifest = [ordered]@{schema='citylife.unity.package-evidence.v1'; utc=[DateTime]::UtcNow.ToString('o'); archive=(Split-Path -Leaf $zip); archiveBytes=(Get-Item -LiteralPath $zip).Length; sha256=$checksum; buildDirectory=('Builds/' + $BuildName); verifiedEntryCount=$packageEntries.Count; entries=$packageEntries}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath ($zip + '.manifest.json') -Encoding utf8
Write-Output $zip
Write-Output "SHA256 $checksum"
Write-Output "Verified $($packageEntries.Count) archive files against the build."
