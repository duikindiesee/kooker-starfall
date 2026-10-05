param([Parameter(Mandatory)][string]$BuildDirectory)
$ErrorActionPreference='Stop'
$rootItem=Get-Item -LiteralPath $BuildDirectory
if (!$rootItem.PSIsContainer -or ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Build must be an ordinary directory.' }
$root=$rootItem.FullName
$items=@(Get-ChildItem -LiteralPath $root -Recurse -Force)
if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Linked build entries cannot ship.' }
$files=@(); $excluded=@()
foreach($file in $items | Where-Object { !$_.PSIsContainer }) {
    $relative=$file.FullName.Substring($root.Length+1).Replace('\','/')
    $parts=$relative.Split('/')
    # Only Unity's top-level, executable-matched backup directory is omitted.
    # Keep its bytes intact and record the omission; all other unsafe files fail.
    $suffix='_BackUpThisFolder_ButDontShipItWithYourGame'
    $backup=$parts.Length -gt 1 -and $parts[0].EndsWith($suffix,[StringComparison]::Ordinal)
    if($backup) {
        $stem=$parts[0].Substring(0,$parts[0].Length-$suffix.Length)
        if (!$stem -or !(Test-Path -LiteralPath (Join-Path $root ($stem+'.exe')) -PathType Leaf)) { throw 'Unmatched Unity backup directory.' }
        $excluded += [pscustomobject]@{path=$relative;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant();reason='Unity explicitly marked do-not-ship backup; original preserved'}
        continue
    }
    & (Join-Path $PSScriptRoot 'check-distribution-path.ps1') -RelativePath $relative
    $files += $file
}
[pscustomobject]@{files=$files;excluded=$excluded}
