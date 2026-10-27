$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$findings = @()
$files = Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter 'appsettings*.json' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]' }
foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($match in [regex]::Matches($text, '"([^"\r\n]+)"\s*:\s*("(?:\\.|[^"\\])*")')) {
        $key = $match.Groups[1].Value
        $value = $match.Groups[2].Value | ConvertFrom-Json
        if ($value -and ($key -match '^(Secret|SecretKey|PublishableKey|AzureRedisConnection|BlobConnection|ServiceBusConnectionString)$' -or $value -match 'SharedAccessKey=|AccountKey=|(?:password|pwd)\s*=')) {
            $findings += $file.Name + ': ' + $key
        }
    }
}
if ($findings.Count) { throw ('Tracked configuration requires cleanup: ' + ($findings -join ', ')) }
Write-Output ('PASS: ' + $files.Count + ' settings files contain no configured credential values.')
