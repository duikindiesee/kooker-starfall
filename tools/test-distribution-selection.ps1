$ErrorActionPreference='Stop'
$tempBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture=Join-Path $tempBase ('starfall-package-test-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $fixture
$selector=Join-Path $PSScriptRoot 'select-distribution-files.ps1'
try {
    $null=New-Item -ItemType File -Path (Join-Path $fixture 'Game.exe')
    $backup=Join-Path $fixture 'Game_BackUpThisFolder_ButDontShipItWithYourGame'
    $null=New-Item -ItemType Directory -Path $backup
    $null=New-Item -ItemType File -Path (Join-Path $backup 'compiler.txt')
    $result=& $selector -BuildDirectory $fixture
    if($result.files.Count -ne 1 -or $result.excluded.Count -ne 1 -or !(Test-Path -LiteralPath (Join-Path $backup 'compiler.txt'))) { throw 'Backup subset/preservation failed.' }
    $private=Join-Path $fixture '.ENV.production'
    $null=New-Item -ItemType File -Path $private
    $denied=$false;try { $null=& $selector -BuildDirectory $fixture } catch {$denied=$true}
    if(!$denied){throw 'Private file passed beside a legitimate backup.'}
    Remove-Item -LiteralPath $private
    $unmatched=Join-Path $fixture 'Other_BackUpThisFolder_ButDontShipItWithYourGame'
    $null=New-Item -ItemType Directory -Path $unmatched
    $null=New-Item -ItemType File -Path (Join-Path $unmatched 'data.txt')
    $denied=$false;try {$null=& $selector -BuildDirectory $fixture} catch {$denied=$true}
    if(!$denied){throw 'Unmatched backup directory was silently excluded.'}
    'PASS: exact backup excluded and preserved; private file and unmatched backup rejected. Synthetic fixtures only.'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(!$resolved.StartsWith($tempBase,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^starfall-package-test-[0-9a-f]{32}$') {throw 'Refusing unsafe fixture cleanup.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
