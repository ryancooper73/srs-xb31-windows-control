#requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$ForceShutdown,
    [string]$LogPath
)

$ErrorActionPreference = 'Stop'

if (-not $DryRun -and -not $ForceShutdown) {
    [Console]::Error.WriteLine(
        'Shutdown: specify -ForceShutdown to permit an actual Windows shutdown')
    exit 2
}

if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $logRoot = if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $PSScriptRoot
    }
    else {
        Join-Path $env:LOCALAPPDATA 'SRS-XB31'
    }
    $LogPath = Join-Path $logRoot 'Shutdown-With-Xb31.log'
}

$script:logEnabled = $true
try {
    $logDirectory = Split-Path -Parent $LogPath
    if (-not [string]::IsNullOrWhiteSpace($logDirectory)) {
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    }
}
catch {
    $script:logEnabled = $false
    [Console]::Error.WriteLine("Shutdown: logging disabled: $($_.Exception.Message)")
}

function Write-ShutdownStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [switch]$ErrorMessage
    )

    if ($script:logEnabled) {
        try {
            $timestampedMessage = '{0:O} {1}' -f [DateTime]::Now, $Message
            Add-Content -LiteralPath $LogPath -Value $timestampedMessage -Encoding UTF8
        }
        catch {
            $script:logEnabled = $false
            [Console]::Error.WriteLine("Shutdown: logging disabled: $($_.Exception.Message)")
        }
    }

    if ($ErrorMessage) {
        [Console]::Error.WriteLine($Message)
    }
    else {
        Write-Output $Message
    }
}

Write-ShutdownStatus 'Shutdown: started'

$xb31Script = Join-Path $PSScriptRoot 'PowerOff-Xb31.ps1'
$powershellPath = Join-Path $PSHOME 'powershell.exe'
$xb31Arguments = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', $xb31Script
)
if ($DryRun) {
    $xb31Arguments += '-Probe'
}

try {
    $xb31Output = @(& $powershellPath @xb31Arguments 2>&1)
    $xb31ExitCode = $LASTEXITCODE
    foreach ($line in $xb31Output) {
        Write-ShutdownStatus $line.ToString()
    }
}
catch {
    Write-ShutdownStatus 'Shutdown: XB31 helper launch failed' -ErrorMessage
    $xb31ExitCode = 14
}

Write-ShutdownStatus "Shutdown: XB31 helper exit $xb31ExitCode"

if ($xb31ExitCode -eq 0) {
    Write-ShutdownStatus 'Shutdown: waiting 2 seconds for XB31 power-off'
    Start-Sleep -Seconds 2
}

if ($DryRun) {
    Write-ShutdownStatus 'Shutdown: Windows shutdown skipped (dry run)'
    exit 0
}

if ($xb31ExitCode -ne 0) {
    Write-ShutdownStatus 'Shutdown: continuing despite XB31 failure' -ErrorMessage
}

$shutdownPath = Join-Path $env:SystemRoot 'System32\shutdown.exe'
try {
    Write-ShutdownStatus 'Shutdown: starting Windows shutdown'
    & $shutdownPath /s /f /t 0
    $shutdownExitCode = $LASTEXITCODE
    Write-ShutdownStatus "Shutdown: Windows shutdown command exit $shutdownExitCode"
}
catch {
    Write-ShutdownStatus 'Shutdown: Windows shutdown launch failed' -ErrorMessage
    exit 1
}

exit $shutdownExitCode
