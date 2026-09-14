param([Parameter(Mandatory)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $BuildDirectory).Path
$pending = [Collections.Generic.Queue[string]]::new()
$pending.Enqueue($root)
$entries = [Collections.Generic.List[string]]::new()
while ($pending.Count -gt 0) {
    $directory = $pending.Dequeue()
    if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked build directory rejected.' }
    foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked build content rejected.' }
        if ($item.PSIsContainer) { $pending.Enqueue($item.FullName); continue }
        $relative = $item.FullName.Substring($root.Length + 1).Replace('\','/')
        if ($relative -match '[\r\n\t]') { throw 'Unsupported filename in fingerprint.' }
        $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $entries.Add($relative + "`t" + $item.Length.ToString([Globalization.CultureInfo]::InvariantCulture) + "`t" + $hash)
    }
}
$entries.Sort([StringComparer]::Ordinal)
$bytes = [Text.Encoding]::UTF8.GetBytes(($entries -join "`n") + "`n")
$digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
[pscustomobject]@{schema='starfall.build-content.v1'; sha256=$digest; files=$entries.Count}
