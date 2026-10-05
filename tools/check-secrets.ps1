# Mirrors CityLife's pinned open-source CLI scan; no Gitleaks Action organisation key.
param([string]$DistributionArchive)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$scanTemp = Join-Path ([IO.Path]::GetTempPath()) ('citylife-secret-scan-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scanTemp
try {
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $archive = 'gitleaks_8.30.1_windows_x64.zip'
        $expectedHash = 'd29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e'
        $binary = Join-Path $scanTemp 'gitleaks.exe'
    } elseif ($IsLinux -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'X64') {
        $archive = 'gitleaks_8.30.1_linux_x64.tar.gz'
        $expectedHash = '551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb'
        $binary = Join-Path $scanTemp 'gitleaks'
    } else { throw 'Pinned scanner is available for Windows x64 and Linux x64 only.' }
    $archivePath = Join-Path $scanTemp $archive
    Invoke-WebRequest -Uri ('https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/' + $archive) -OutFile $archivePath
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) { throw 'Gitleaks release checksum mismatch.' }
    if ($archive.EndsWith('.zip')) { Expand-Archive -LiteralPath $archivePath -DestinationPath $scanTemp }
    else { & tar -xzf $archivePath -C $scanTemp gitleaks; if ($LASTEXITCODE -ne 0) { throw 'Could not unpack verified Gitleaks.' } }
    Push-Location $projectRoot
    try {
        $shallow = & git rev-parse --is-shallow-repository
        if ($LASTEXITCODE -ne 0 -or $shallow -ne 'false') { throw 'Full-history scan requires a non-shallow Git checkout (fetch-depth: 0).' }
        & $binary git '--log-opts=--all' '--redact=100' --verbose --no-banner --no-color --ignore-gitleaks-allow .
        if ($LASTEXITCODE -ne 0) { throw 'Gitleaks full-history scan failed. Review redacted findings; rotate a real exposed credential before history remediation.' }
        # Also catch staged/untracked publishable files before the first implementation commit.
        $candidateFolder = Join-Path $scanTemp 'publishable'
        $null = New-Item -ItemType Directory -Path $candidateFolder
        $candidates = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
        if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate publishable files.' }
        foreach ($relative in $candidates) {
            if ($relative -match '[\x00-\x1f"]') { throw 'Control-character or quoted file names require cleanup before a public scan.' }
            $source = [IO.Path]::GetFullPath((Join-Path $projectRoot $relative))
            if (-not $source.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate escaped project root.' }
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
            # Git includes dotfiles; PowerShell on Linux needs -Force to inspect them.
            if ((Get-Item -LiteralPath $source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Do not publish symlinked runtime state.' }
            $destination = Join-Path $candidateFolder $relative
            $null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination))
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
        & $binary dir '--redact=100' --verbose --no-banner --no-color --ignore-gitleaks-allow $candidateFolder
        if ($LASTEXITCODE -ne 0) { throw 'Gitleaks publishable-file scan failed. No findings artifact is uploaded.' }
        if ($DistributionArchive) {
            $distribution = [IO.Path]::GetFullPath((Join-Path $projectRoot $DistributionArchive))
            $buildRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Builds')) + [IO.Path]::DirectorySeparatorChar
            if (-not $distribution.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($distribution) -ne '.zip' -or -not (Test-Path -LiteralPath $distribution -PathType Leaf)) {
                throw 'Distribution archive must be an existing ZIP under this project Builds directory.'
            }
            $manifestPath = $distribution + '.manifest.json'
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            if ($manifest.schema -ne 'citylife.unity.package-evidence.v1' -or $manifest.sha256 -ne (Get-FileHash -LiteralPath $distribution -Algorithm SHA256).Hash.ToLowerInvariant() -or $manifest.archiveBytes -ne (Get-Item -LiteralPath $distribution).Length) { throw 'Distribution does not match its integrity manifest.' }
            $distributionFolder = Join-Path $scanTemp 'distribution'
            $null = New-Item -ItemType Directory -Path $distributionFolder
            $expectedEntries = @{}
            foreach ($record in $manifest.entries) {
                if ($expectedEntries.ContainsKey($record.path)) { throw 'Duplicate manifest path.' }
                $expectedEntries[$record.path] = $record
            }
            $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            $package = [IO.Compression.ZipFile]::OpenRead($distribution)
            try {
                foreach ($entry in $package.Entries) {
                    if (-not $entry.Name) { continue }
                    $name = $entry.FullName.Replace('\', '/')
                    if ($name -match '(^/|:|(^|/)\.\.(/|$)|[\x00-\x1f]|BackUpThisFolder_ButDontShipItWithYourGame)' -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe distribution entry.' }
                    if (-not $seen.Add($name) -or -not $expectedEntries.ContainsKey($name) -or $entry.Length -ne $expectedEntries[$name].bytes) { throw 'Distribution inventory mismatch.' }
                    $destination = [IO.Path]::GetFullPath((Join-Path $distributionFolder $name))
                    if (-not $destination.StartsWith($distributionFolder + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Distribution path escaped temporary directory.' }
                    $null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination))
                    $inputStream = $entry.Open()
                    $outputStream = [IO.File]::Create($destination)
                    try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
                    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedEntries[$name].sha256) { throw 'Extracted distribution entry differs from its manifest.' }
                }
            } finally { $package.Dispose() }
            if ($seen.Count -ne $manifest.verifiedEntryCount -or $seen.Count -ne $expectedEntries.Count) { throw 'Distribution omitted manifest entries.' }
            # Exact vendor-file checks precede three exact file/rule/line exceptions. These
            # are Mono strong-name PUBLIC-key mappings, verified against the installed editor.
            # Any vendor-byte change fails closed and requires a fresh review.
            $vendorRecords = @(
                @{path='MonoBleedingEdge/etc/mono/2.0/machine.config'; line=214; sha256='e91782a27fa39fc6c1d6ee8b08529f5d35052310d0006034b878eb04b8f2af30'},
                @{path='MonoBleedingEdge/etc/mono/4.0/machine.config'; line=231; sha256='e60aec2c5115d65b3acb3c55ea21576dbd770f579166c017125571e46ae560ed'},
                @{path='MonoBleedingEdge/etc/mono/4.5/machine.config'; line=234; sha256='ee950004b576fb28dc85f4b0435ed04bf96612de2e8b53be84d07afe85a0de6c'}
            )
            function Assert-ReviewedVendorMetadata([string]$Path, [string]$ExpectedHash) {
                if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $ExpectedHash) { throw 'Vendor public-key metadata changed; no exception is permitted.' }
            }
            $fingerprints = foreach ($record in $vendorRecords) {
                $vendorFile = Join-Path $distributionFolder $record.path
                Assert-ReviewedVendorMetadata $vendorFile $record.sha256
                $vendorFile.Replace('\', '/') + ':generic-api-key:' + $record.line
            }
            $vendorCanary = Join-Path $scanTemp 'modified-vendor-canary.txt'
            [IO.File]::WriteAllText($vendorCanary, 'Changed bytes must never inherit a vendor exception.')
            $vendorRejected = $false
            try { Assert-ReviewedVendorMetadata $vendorCanary $vendorRecords[0].sha256 } catch { $vendorRejected = $true }
            if (-not $vendorRejected) { throw 'Modified-vendor canary was incorrectly exempted.' }
            $fingerprintFile = Join-Path $scanTemp 'distribution-exact-fingerprints.ignore'
            Set-Content -LiteralPath $fingerprintFile -Value $fingerprints -Encoding utf8
            # Gitleaks archive detection misclassifies Mono's plain-text *.browser XML as
            # Brotli. Scan the verified extraction with nesting off, then explicitly scan
            # those three text files through stdin so no known skipped text remains.
            & $binary dir '--redact=100' --verbose --no-banner --no-color --ignore-gitleaks-allow '--max-archive-depth=0' --gitleaks-ignore-path $fingerprintFile $distributionFolder
            if ($LASTEXITCODE -ne 0) { throw 'Gitleaks distribution-archive scan failed. No findings artifact is uploaded.' }
            foreach ($version in @('2.0', '4.0', '4.5')) {
                $browser = Join-Path $distributionFolder ('MonoBleedingEdge/etc/mono/' + $version + '/Browsers/Compat.browser')
                Get-Content -LiteralPath $browser -Raw -Encoding utf8 | & $binary stdin '--redact=100' --no-banner --no-color --ignore-gitleaks-allow
                if ($LASTEXITCODE -ne 0) { throw 'Mono browser-definition text scan failed.' }
            }
            Write-Output ('Final distribution passed: ' + $seen.Count + ' byte-verified extracted entries; 3 exact vendor public-key exceptions after whole-file hash checks; 3 browser XML files explicitly scanned; modified-vendor canary rejected.')
            Write-Output ('Distribution SHA256 ' + $manifest.sha256)
        }
        # An ephemeral synthetic credential proves that a disabled/misconfigured scanner fails
        # this check. Its value is created at runtime, never committed, and fully redacted.
        $canaryFolder = Join-Path $scanTemp 'canary'
        $null = New-Item -ItemType Directory -Path $canaryFolder
        $canary = 'gh' + 'p_' + [Guid]::NewGuid().ToString('N') + '01234567'
        Set-Content -LiteralPath (Join-Path $canaryFolder 'fixture.txt') -Value $canary
        & $binary dir '--redact=100' --no-banner --no-color '--log-level=error' $canaryFolder
        if ($LASTEXITCODE -ne 1) { throw 'Gitleaks synthetic detection canary did not reject the fixture.' }
        # GitHub's pwsh wrapper exits with LASTEXITCODE; the canary's expected rejection is
        # a successful guard test, not a failed scan of the project.
        $global:LASTEXITCODE = 0
        Write-Output 'Gitleaks 8.30.1 passed: every reachable commit and current publishable files. Detected values were fully redacted.'
    } finally { Pop-Location }
} finally {
    # Cleanup only our exact GUID-named temporary directory, after checking its absolute parent.
    $resolvedTemp = [IO.Path]::GetFullPath($scanTemp)
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolvedTemp) -ne $expectedParent -or [IO.Path]::GetFileName($resolvedTemp) -notmatch '^citylife-secret-scan-[a-f0-9]{32}$') { throw 'Refusing unsafe temporary cleanup.' }
    Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
}
