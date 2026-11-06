$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$sourceScriptPath = Join-Path $repositoryRoot 'Shutdown-With-Xb31.ps1'
$tempRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$tempLeaf = "SRS-XB31-Shutdown-Failure-$([Guid]::NewGuid().ToString('N'))"
$candidateTempDirectory = [IO.Path]::GetFullPath((Join-Path $tempRoot $tempLeaf))
$validatedTempDirectory = $null

if ([IO.Path]::GetDirectoryName($candidateTempDirectory) -ine $tempRoot) {
    throw "FAIL: temporary fixture path is not directly beneath the temp root: $candidateTempDirectory"
}

if (Test-Path -LiteralPath $candidateTempDirectory) {
    throw "FAIL: unique temporary fixture path already exists: $candidateTempDirectory"
}

try {
    New-Item -ItemType Directory -Path $candidateTempDirectory | Out-Null
    $resolvedTempDirectory = (Resolve-Path -LiteralPath $candidateTempDirectory).Path
    $tempDirectoryItem = Get-Item -LiteralPath $resolvedTempDirectory -Force

    if ($resolvedTempDirectory -ine $candidateTempDirectory -or
        [IO.Path]::GetDirectoryName($resolvedTempDirectory) -ine $tempRoot -or
        ($tempDirectoryItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "FAIL: temporary fixture directory did not resolve to the validated location: $resolvedTempDirectory"
    }

    $validatedTempDirectory = $resolvedTempDirectory
    $copiedScriptPath = Join-Path $validatedTempDirectory 'Shutdown-With-Xb31.ps1'
    $fixtureHelperPath = Join-Path $validatedTempDirectory 'PowerOff-Xb31.ps1'
    $logPath = Join-Path $validatedTempDirectory 'Shutdown-With-Xb31.log'
    $notificationScriptPath = Join-Path $validatedTempDirectory 'Notify.ps1'
    $notificationMarkerPath = Join-Path $validatedTempDirectory 'notification.txt'
    $failingNotificationScriptPath = Join-Path $validatedTempDirectory 'Notify-Failure.ps1'
    $failingNotificationMarkerPath = Join-Path $validatedTempDirectory 'notification-failure.txt'
    $shutdownScriptPath = Join-Path $validatedTempDirectory 'Shutdown.ps1'
    $shutdownMarkerPath = Join-Path $validatedTempDirectory 'shutdown.txt'

    Copy-Item -LiteralPath $sourceScriptPath -Destination $copiedScriptPath
    @'
param([switch]$Probe)
exit 10
'@ | Set-Content -LiteralPath $fixtureHelperPath -Encoding ASCII

    @"
param([string]`$Message)
Set-Content -LiteralPath '$notificationMarkerPath' -Value `$Message
exit 0
"@ | Set-Content -LiteralPath $notificationScriptPath -Encoding ASCII

    @"
param([string]`$Message)
Set-Content -LiteralPath '$failingNotificationMarkerPath' -Value `$Message
exit 9
"@ | Set-Content -LiteralPath $failingNotificationScriptPath -Encoding ASCII

    @"
param([Parameter(ValueFromRemainingArguments = `$true)]`$Arguments)
Set-Content -LiteralPath '$shutdownMarkerPath' -Value 'invoked'
exit 0
"@ | Set-Content -LiteralPath $shutdownScriptPath -Encoding ASCII

    $uiProcessName = 'Xb31.Control'
    $existingUiProcessIds = @(
        Get-Process -Name $uiProcessName -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Id }
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $copiedScriptPath `
                -ForceShutdown `
                -LogPath $logPath `
                -NotificationScriptPath $notificationScriptPath `
                -ShutdownCommandPath $shutdownScriptPath 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $newUiProcesses = @(
        Get-Process -Name $uiProcessName -ErrorAction SilentlyContinue |
            Where-Object { $existingUiProcessIds -notcontains $_.Id }
    )

    if ($exitCode -ne 10) {
        throw "FAIL: helper-failure dry run exited with $exitCode instead of the helper failure code 10. Output: $($output -join ' | ')"
    }

    foreach ($expectedText in @(
        'Shutdown: XB31 helper exit 10',
        'Shutdown: XB31 power off failed; Windows remains on',
        'Shutdown: failure notification displayed'
    )) {
        if ($output -notcontains $expectedText) {
            throw "FAIL: helper-failure dry run omitted '$expectedText'. Output: $($output -join ' | ')"
        }
    }

    if (-not (Test-Path -LiteralPath $notificationMarkerPath -PathType Leaf)) {
        throw 'FAIL: helper failure did not invoke the visible notification action'
    }

    if (Test-Path -LiteralPath $shutdownMarkerPath -PathType Leaf) {
        throw 'FAIL: helper failure invoked the controlled Windows shutdown command'
    }

    if ($newUiProcesses.Count -ne 0) {
        $processIds = @($newUiProcesses | ForEach-Object { $_.Id }) -join ', '
        throw "FAIL: helper-failure dry run started the XB31 control UI process(es): $processIds"
    }

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $notificationFailureOutput = @(
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $copiedScriptPath `
                -ForceShutdown `
                -LogPath $logPath `
                -NotificationScriptPath $failingNotificationScriptPath `
                -ShutdownCommandPath $shutdownScriptPath 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $notificationFailureExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($notificationFailureExitCode -ne 10) {
        throw "FAIL: notification failure changed the helper exit code. Output: $($notificationFailureOutput -join ' | ')"
    }

    if (-not (Test-Path -LiteralPath $failingNotificationMarkerPath -PathType Leaf)) {
        throw 'FAIL: notification fallback test did not invoke the controlled notification action'
    }

    if (($notificationFailureOutput -join ' ') -notmatch 'Shutdown: failure notification unavailable:') {
        throw "FAIL: notification failure did not report the fallback diagnostic. Output: $($notificationFailureOutput -join ' | ')"
    }

    if (Test-Path -LiteralPath $shutdownMarkerPath -PathType Leaf) {
        throw 'FAIL: notification failure invoked the controlled Windows shutdown command'
    }

    Write-Output 'PASS: failed XB31 power off displayed a notification, kept Windows on, and returned the helper failure code'
}
finally {
    if ($null -ne $validatedTempDirectory -and (Test-Path -LiteralPath $validatedTempDirectory)) {
        $cleanupPath = (Resolve-Path -LiteralPath $validatedTempDirectory).Path
        $cleanupItem = Get-Item -LiteralPath $cleanupPath -Force
        if ($cleanupPath -ine $validatedTempDirectory -or
            [IO.Path]::GetDirectoryName($cleanupPath) -ine $tempRoot -or
            -not ([IO.Path]::GetFileName($cleanupPath).StartsWith(
                'SRS-XB31-Shutdown-Failure-',
                [StringComparison]::Ordinal)) -or
            ($cleanupItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "FAIL: refusing to remove unvalidated temporary path: $cleanupPath"
        }

        Remove-Item -LiteralPath $cleanupPath -Recurse -Force
    }
}
