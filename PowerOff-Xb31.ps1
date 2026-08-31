#requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$VerifyFrame,
    [switch]$Probe,
    [switch]$VerboseDiagnostics
)

$ErrorActionPreference = 'Stop'

$helperPath = Join-Path $PSScriptRoot 'src\Xb31.PowerOff\bin\Release\net8.0-windows10.0.26100.0\Xb31PowerOff.exe'
if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    [Console]::Error.WriteLine('XB31: helper is not built; run dotnet build -c Release')
    exit 14
}

$helperArguments = @()
if ($VerifyFrame) {
    $helperArguments += '--verify-frame'
}
if ($Probe) {
    $helperArguments += '--probe'
}
if ($VerboseDiagnostics) {
    $helperArguments += '--verbose'
}

try {
    & $helperPath @helperArguments
    $helperExitCode = $LASTEXITCODE
}
catch {
    [Console]::Error.WriteLine('XB31: helper launch failed')
    exit 14
}

if ($helperExitCode -notin @(0, 10, 11, 12, 13, 14)) {
    [Console]::Error.WriteLine('XB31: unexpected helper failure')
    exit 14
}

exit $helperExitCode
