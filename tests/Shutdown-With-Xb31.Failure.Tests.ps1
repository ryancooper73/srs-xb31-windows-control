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

    Copy-Item -LiteralPath $sourceScriptPath -Destination $copiedScriptPath
    @'
param([switch]$Probe)
exit 10
'@ | Set-Content -LiteralPath $fixtureHelperPath -Encoding ASCII

    $uiProcessName = 'Xb31.Control'
    $existingUiProcessIds = @(
        Get-Process -Name $uiProcessName -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Id }
    )

    $output = @(
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $copiedScriptPath -DryRun -LogPath $logPath 2>&1 |
            ForEach-Object { $_.ToString() }
    )
    $exitCode = $LASTEXITCODE

    $newUiProcesses = @(
        Get-Process -Name $uiProcessName -ErrorAction SilentlyContinue |
            Where-Object { $existingUiProcessIds -notcontains $_.Id }
    )

    if ($exitCode -ne 0) {
        throw "FAIL: helper-failure dry run exited with $exitCode. Output: $($output -join ' | ')"
    }

    foreach ($expectedText in @(
        'Shutdown: XB31 helper exit 10',
        'Shutdown: Windows shutdown skipped (dry run)'
    )) {
        if ($output -notcontains $expectedText) {
            throw "FAIL: helper-failure dry run omitted '$expectedText'. Output: $($output -join ' | ')"
        }
    }

    if ($newUiProcesses.Count -ne 0) {
        $processIds = @($newUiProcesses | ForEach-Object { $_.Id }) -join ', '
        throw "FAIL: helper-failure dry run started the XB31 control UI process(es): $processIds"
    }

    Write-Output 'PASS: shutdown remained headless and skipped Windows shutdown after helper exit 10'
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
