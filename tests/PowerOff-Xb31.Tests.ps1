$ErrorActionPreference = 'Stop'

$helperPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'PowerOff-Xb31.ps1'
$expectedFrameHex = '3e0000000000053000000f00443c'

if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    throw "FAIL: helper is missing: $helperPath"
}

$expectedExecutable = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Xb31.PowerOff\bin\Release\net8.0-windows10.0.26100.0\Xb31PowerOff.exe'
if (-not (Test-Path -LiteralPath $expectedExecutable -PathType Leaf)) {
    throw "FAIL: headless helper output path changed: $expectedExecutable"
}

$output = @(
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $helperPath -VerifyFrame 2>&1 |
        ForEach-Object { $_.ToString() }
)
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    throw "FAIL: frame verification exited with $exitCode. Output: $($output -join ' | ')"
}

if ($output.Count -ne 1 -or $output[0] -cne $expectedFrameHex) {
    throw "FAIL: expected only '$expectedFrameHex', got '$($output -join ' | ')'"
}

Write-Output 'PASS: exact sequence-zero power-off frame verified'
