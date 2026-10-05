$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$files = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tools') -Filter '*.ps1' -File
foreach ($file in $files) {
    $tokens = $null
    $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) {
        foreach ($problem in $parseErrors) { Write-Error ($file.Name + ':' + $problem.Extent.StartLineNumber + ' ' + $problem.Message) }
        throw 'PowerShell syntax check failed.'
    }
}
Write-Output ('PowerShell syntax passed: ' + $files.Count + ' scripts. No scripts were executed.')
