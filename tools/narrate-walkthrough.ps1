param(
    [Parameter(Mandatory=$true)][string]$TextPath,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$Voice = 'Microsoft David Desktop',
    [ValidateRange(-10,10)][int]$Rate = 0
)
$ErrorActionPreference = 'Stop'
# Local speech only: no service, playback, device setting or network changes.
$sourcePath = (Resolve-Path -LiteralPath $TextPath).Path
$targetPath = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($targetPath) -ne '.wav') { throw 'Output must be a WAV file.' }
$receiptPath = $targetPath + '.json'
if ((Test-Path -LiteralPath $targetPath) -or (Test-Path -LiteralPath $receiptPath)) {
    throw 'Output or receipt exists; choose a new versioned filename.'
}
$narration = [IO.File]::ReadAllText($sourcePath)
if ([string]::IsNullOrWhiteSpace($narration)) { throw 'Narration text is empty.' }
if ($narration -match '(?im)^\s*#{1,6}\s|\[(?:Insert|Name)\b|conditional on final|not release acceptance') {
    throw 'Narration still contains draft headings or unresolved acceptance placeholders. Supply reviewed spoken text only.'
}
$targetDirectory = [IO.Path]::GetDirectoryName($targetPath)
if (-not (Test-Path -LiteralPath $targetDirectory -PathType Container)) {
    throw 'Create the intended output directory first.'
}
Add-Type -AssemblyName System.Speech
$speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $speaker.SelectVoice($Voice)
    $speaker.Rate = $Rate
    $speaker.SetOutputToWaveFile($targetPath)
    $speaker.Speak($narration)
} finally {
    $speaker.Dispose()
}
$output = Get-Item -LiteralPath $targetPath
if ($output.Length -le 44) { throw 'Speech synthesis produced no usable audio payload.' }
$receipt = [ordered]@{
    schema = 'starfall.walkthrough.narration.v1'
    status = 'GENERATED_REPLAY_PENDING'
    engine = 'Windows System.Speech offline'
    voice = $Voice
    rate = $Rate
    source = $sourcePath
    sourceSha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    output = $targetPath
    outputSha256 = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
    bytes = $output.Length
    utc = [DateTime]::UtcNow.ToString('o')
    boundary = 'Generated audio only. Listen and verify final video synchronization before acceptance.'
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8
$receipt | ConvertTo-Json
