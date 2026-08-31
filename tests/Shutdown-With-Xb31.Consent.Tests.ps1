$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$sourceScriptPath = Join-Path $repositoryRoot 'Shutdown-With-Xb31.ps1'
$tempRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$tempLeaf = "SRS-XB31-Shutdown-Consent-$([Guid]::NewGuid().ToString('N'))"
$candidateTempDirectory = [IO.Path]::GetFullPath((Join-Path $tempRoot $tempLeaf))
$validatedTempDirectory = $null

if ([IO.Path]::GetDirectoryName($candidateTempDirectory) -ine $tempRoot) {
    throw "FAIL: temporary fixture path is not directly beneath the temp root: $candidateTempDirectory"
}

try {
    New-Item -ItemType Directory -Path $candidateTempDirectory | Out-Null
    $validatedTempDirectory = (Resolve-Path -LiteralPath $candidateTempDirectory).Path
    $copiedScriptPath = Join-Path $validatedTempDirectory 'Shutdown-With-Xb31.ps1'
    $fixtureHelperPath = Join-Path $validatedTempDirectory 'PowerOff-Xb31.ps1'
    $helperMarkerPath = Join-Path $validatedTempDirectory 'helper-invoked.txt'
    $fakeWindowsRoot = Join-Path $validatedTempDirectory 'Windows'
    $standardOutputPath = Join-Path $validatedTempDirectory 'stdout.txt'
    $standardErrorPath = Join-Path $validatedTempDirectory 'stderr.txt'

    Copy-Item -LiteralPath $sourceScriptPath -Destination $copiedScriptPath
    New-Item -ItemType Directory -Path (Join-Path $fakeWindowsRoot 'System32') | Out-Null

    @"
param([switch]`$Probe)
Set-Content -LiteralPath '$helperMarkerPath' -Value 'invoked'
exit 0
"@ | Set-Content -LiteralPath $fixtureHelperPath -Encoding ASCII

    $childCommand = "`$env:SystemRoot = '$fakeWindowsRoot'; & '$copiedScriptPath'"
    $process = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $childCommand `
        -RedirectStandardOutput $standardOutputPath `
        -RedirectStandardError $standardErrorPath `
        -Wait `
        -PassThru
    $output = @(
        Get-Content -LiteralPath $standardOutputPath, $standardErrorPath -ErrorAction SilentlyContinue
    )
    $exitCode = $process.ExitCode

    if ($exitCode -eq 0) {
        throw "FAIL: missing consent returned success. Output: $($output -join ' | ')"
    }

    if (Test-Path -LiteralPath $helperMarkerPath) {
        throw 'FAIL: missing consent still invoked the XB31 helper'
    }

    $expected = 'Shutdown: specify -ForceShutdown to permit an actual Windows shutdown'
    if ($output -notcontains $expected) {
        throw "FAIL: missing consent did not explain the safeguard. Output: $($output -join ' | ')"
    }

    Write-Output 'PASS: combined shutdown requires explicit consent before any side effect'
}
finally {
    if ($null -ne $validatedTempDirectory -and (Test-Path -LiteralPath $validatedTempDirectory)) {
        $cleanupPath = (Resolve-Path -LiteralPath $validatedTempDirectory).Path
        if ([IO.Path]::GetDirectoryName($cleanupPath) -ine $tempRoot -or
            -not ([IO.Path]::GetFileName($cleanupPath).StartsWith(
                'SRS-XB31-Shutdown-Consent-',
                [StringComparison]::Ordinal))) {
            throw "FAIL: refusing to remove unvalidated temporary path: $cleanupPath"
        }

        Remove-Item -LiteralPath $cleanupPath -Recurse -Force
    }
}
