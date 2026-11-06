#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$DesktopPath = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::DesktopDirectory),
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = $PSScriptRoot
}

if ([string]::IsNullOrWhiteSpace($DesktopPath)) {
    throw 'Desktop path is unavailable.'
}

if (-not (Test-Path -LiteralPath $DesktopPath -PathType Container)) {
    throw "Desktop directory does not exist: $DesktopPath"
}

$shutdownScriptPath = Join-Path $RepositoryRoot 'Shutdown-With-Xb31.ps1'
if (-not (Test-Path -LiteralPath $shutdownScriptPath -PathType Leaf)) {
    throw "Shutdown script is missing: $shutdownScriptPath"
}

$launcherPath = Join-Path $DesktopPath 'Shutdown XB31 and Windows.bat'
$launcherContent = @"
@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$shutdownScriptPath" -ForceShutdown
exit /b %ERRORLEVEL%
"@

$temporaryLauncherPath = Join-Path $DesktopPath (
    '.Shutdown-XB31-{0}.tmp' -f [Guid]::NewGuid().ToString('N'))
try {
    [IO.File]::WriteAllText(
        $temporaryLauncherPath,
        $launcherContent,
        [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryLauncherPath -Destination $launcherPath -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryLauncherPath -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryLauncherPath -Force
    }
}

Write-Output $launcherPath
