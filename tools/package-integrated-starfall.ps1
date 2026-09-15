param(
    [Parameter(Mandatory)][string]$BuildManifest,
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][string]$ReleaseReadme
)
$ErrorActionPreference = 'Stop'
# Local packaging only. Passing this tool is not review, publication or user acceptance.
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$identity = & (Join-Path $PSScriptRoot 'check-integrated-build.ps1') -BuildManifest $BuildManifest -RuntimeDirectory $RuntimeDirectory | ConvertFrom-Json
$buildRoot = Join-Path $root ('Builds/' + $identity.build)
$readmeItem = Get-Item -LiteralPath $ReleaseReadme
if ($readmeItem.PSIsContainer -or ($readmeItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Release README must be an ordinary file.' }
$readme = Get-Content -LiteralPath $ReleaseReadme -Raw
if (-not $readme.Contains($identity.build) -or -not $readme.Contains($identity.source)) { throw 'README must identify the exact tested build and source.' }
$documents = [ordered]@{
    'RELEASE-README.txt' = $readmeItem.FullName
    'THIRD-PARTY-NOTICES.txt' = Join-Path $root 'Assets/CityLife/Art/THIRD-PARTY-NOTICES.txt'
    'CC0-1.0.txt' = Join-Path $root 'Assets/CityLife/Art/Licenses/CC0-1.0.txt'
}
foreach ($path in $documents.Values) {
    $item = Get-Item -LiteralPath $path
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Package document is missing or linked.' }
}
$output = Join-Path $root 'Builds/Download'
if (Test-Path -LiteralPath $output) {
    $item = Get-Item -LiteralPath $output
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Download must be an ordinary directory.' }
} else { $null = New-Item -ItemType Directory -Path $output }
$zipPath = Join-Path $output ($identity.build + '-Windows.zip')
$receiptPath = $zipPath + '.manifest.json'
if ((Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath $receiptPath)) { throw 'Refusing to overwrite a preserved package.' }
# Only the fingerprinted player directory and explicit documents enter the ZIP.
# Never sweep repository evidence, saves, service configuration or credentials.
$files = @(Get-ChildItem -LiteralPath $buildRoot -File -Recurse -Force)
foreach ($file in $files) {
    if ($file.Extension -match '^\.(log|db|sqlite|sqlite3|env|pem|key)$' -or $file.Name -match '(?i)credential|secret|^\.env') { throw "Unexpected private/runtime file in build: $($file.Name)" }
}
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($buildRoot.Length + 1).Replace('\','/')
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $identity.build + '/' + $relative, [IO.Compression.CompressionLevel]::Optimal)
    }
    foreach ($name in $documents.Keys) {
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $documents[$name], $name, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $zip.Dispose(); $stream.Dispose() }
$after = & (Join-Path $PSScriptRoot 'get-integrated-content-hash.ps1') -BuildDirectory $buildRoot
if ($after.sha256 -ne $identity.buildContentSha256) { throw 'Build changed during packaging; ZIP is unverified and must not be distributed.' }
$verify = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($verify.Entries.Count -ne $files.Count + $documents.Count) { throw 'Archive entry count mismatch.' }
    foreach ($entry in $verify.Entries) {
        $source = if ($entry.FullName.StartsWith($identity.build + '/')) { Join-Path $buildRoot $entry.FullName.Substring($identity.build.Length + 1) } else { $documents[$entry.FullName] }
        if (-not $source) { throw 'Unexpected archive entry.' }
        $inputStream = $entry.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($inputStream)) } finally { $inputStream.Dispose() }
        if ($actual -ne (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) { throw 'Archive content verification failed.' }
    }
} finally { $verify.Dispose() }
[ordered]@{
    status = 'LOCAL_PACKAGE_BYTES_VERIFIED_NOT_RELEASE_ACCEPTANCE'
    build = $identity.build
    sourceCommit = $identity.source
    buildContentSha256 = $identity.buildContentSha256
    archive = $zipPath
    archiveSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    entries = $files.Count + $documents.Count
    scope = 'No services, model weights, saves or private configuration included. Review and user acceptance remain separate.'
} | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8
Get-Content -LiteralPath $receiptPath
& (Join-Path $PSScriptRoot 'retain-current-integrated-build.ps1') -BuildManifest $BuildManifest -RuntimeDirectory $RuntimeDirectory -Execute | Out-Host
