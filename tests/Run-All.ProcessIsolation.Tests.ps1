$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) {
    throw 'FAIL: Windows system root is unavailable'
}

$commandPromptPath = Join-Path $env:SystemRoot 'System32\cmd.exe'
if (-not (Test-Path -LiteralPath $commandPromptPath -PathType Leaf)) {
    throw "FAIL: command prompt executable is missing: $commandPromptPath"
}

& $commandPromptPath /d /c exit 7
if ($LASTEXITCODE -ne 7) {
    throw "FAIL: stale native exit fixture expected 7, got $LASTEXITCODE"
}

Write-Output 'PASS: isolated PowerShell test returned normally after a stale native exit code'
