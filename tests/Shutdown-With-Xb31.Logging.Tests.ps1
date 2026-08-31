$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$sourceScriptPath = Join-Path $repositoryRoot 'Shutdown-With-Xb31.ps1'
$tempRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$tempLeaf = "SRS-XB31-Shutdown-Logging-$([Guid]::NewGuid().ToString('N'))"
$candidateTempDirectory = [IO.Path]::GetFullPath((Join-Path $tempRoot $tempLeaf))
$validatedTempDirectory = $null

if (-not (Test-Path -LiteralPath $sourceScriptPath -PathType Leaf)) {
    throw "FAIL: shutdown orchestrator is missing: $sourceScriptPath"
}

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
[CmdletBinding()]
param([switch]$Probe)

if (-not $Probe) {
    [Console]::Error.WriteLine('XB31 fixture: expected -Probe')
    exit 64
}

Write-Output 'XB31: probe complete; no data sent'
exit 0
'@ | Set-Content -LiteralPath $fixtureHelperPath -Encoding ASCII

    foreach ($fixturePath in @($copiedScriptPath, $fixtureHelperPath)) {
        if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) {
            throw "FAIL: isolated fixture file is missing: $fixturePath"
        }

        $resolvedFixturePath = (Resolve-Path -LiteralPath $fixturePath).Path
        $fixtureItem = Get-Item -LiteralPath $resolvedFixturePath -Force
        if ($resolvedFixturePath -ine $fixturePath -or
            [IO.Path]::GetDirectoryName($resolvedFixturePath) -ine $validatedTempDirectory -or
            ($fixtureItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "FAIL: isolated fixture file did not resolve inside the validated directory: $resolvedFixturePath"
        }
    }

    $output = @(
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $copiedScriptPath -DryRun -LogPath $logPath 2>&1 |
            ForEach-Object { $_.ToString() }
    )
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "FAIL: logging dry run exited with $exitCode. Output: $($output -join ' | ')"
    }

    if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
        throw "FAIL: shutdown orchestrator did not create the requested log: $logPath"
    }

    $logText = Get-Content -LiteralPath $logPath -Raw
    foreach ($expectedText in @(
        'XB31: probe complete; no data sent',
        'Shutdown: XB31 helper exit 0',
        'Shutdown: waiting 2 seconds for XB31 power-off',
        'Shutdown: Windows shutdown skipped (dry run)'
    )) {
        if ($logText -notmatch [Regex]::Escape($expectedText)) {
            throw "FAIL: shutdown log omitted '$expectedText'. Log: $logText"
        }
    }

    Write-Output 'PASS: combined shutdown persisted helper and orchestration results'
}
finally {
    if ($null -ne $validatedTempDirectory -and (Test-Path -LiteralPath $validatedTempDirectory)) {
        $cleanupPath = (Resolve-Path -LiteralPath $validatedTempDirectory).Path
        $cleanupItem = Get-Item -LiteralPath $cleanupPath -Force
        if ($cleanupPath -ine $validatedTempDirectory -or
            [IO.Path]::GetDirectoryName($cleanupPath) -ine $tempRoot -or
            -not ([IO.Path]::GetFileName($cleanupPath).StartsWith(
                'SRS-XB31-Shutdown-Logging-',
                [StringComparison]::Ordinal)) -or
            ($cleanupItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "FAIL: refusing to remove unvalidated temporary path: $cleanupPath"
        }

        Remove-Item -LiteralPath $cleanupPath -Recurse -Force
    }
}
