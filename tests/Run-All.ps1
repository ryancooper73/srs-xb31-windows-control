$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) {
    throw 'Windows system root is unavailable'
}

$windowsPowerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $windowsPowerShellPath -PathType Leaf)) {
    throw "Windows PowerShell executable is missing: $windowsPowerShellPath"
}

& dotnet build (Join-Path $root 'SRS-XB31.slnx') --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& dotnet test (Join-Path $root 'tests\Xb31.Core.Tests\Xb31.Core.Tests.csproj') --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& dotnet test (Join-Path $root 'tests\Xb31.Control.Tests\Xb31.Control.Tests.csproj') --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.Tests.ps1' |
    Sort-Object Name |
    ForEach-Object {
        & $windowsPowerShellPath -NoProfile -ExecutionPolicy Bypass -File $_.FullName
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

Write-Output 'PASS: complete XB31 offline and DryRun verification'
