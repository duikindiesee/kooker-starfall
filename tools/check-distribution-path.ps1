param([Parameter(Mandatory)][string]$RelativePath)
$ErrorActionPreference = 'Stop'
# Pure filename policy: no filesystem traversal, extraction, deletion or ZIP creation.
$name = $RelativePath.Replace('\','/')
if (!$name -or $name.StartsWith('/') -or $name -match '[:\x00-\x1f]') { throw 'Unsafe distribution path.' }
$parts = $name.Split('/')
foreach ($part in $parts) {
    if (!$part -or $part -eq '.' -or $part -eq '..' -or $part -match '[ .]$') { throw 'Ambiguous distribution path.' }
    if ($part -match '^(?i:\.git|Library|UserSettings|Worlds|Sessions|Saves?|Memory|Ledger|Evidence|Logs)$' -or
        $part -match '(?i)BackUpThisFolder_ButDontShipItWithYourGame' -or
        $part -match '(?i)credential|secret|^\.env') { throw 'Private state cannot enter distribution.' }
}
if ([IO.Path]::GetExtension($parts[-1]) -match '^\.(?i:ulf|alf|pem|key|p12|pfx|jks|keystore|sqlite|sqlite3|db|db3|log|jsonl|env|dmp|zip|7z|rar)$') {
    throw 'Private/runtime or nested archive file cannot enter distribution.'
}
