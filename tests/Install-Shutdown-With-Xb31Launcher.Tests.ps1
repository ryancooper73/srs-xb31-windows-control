$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$installerPath = Join-Path $repositoryRoot 'Install-Shutdown-With-Xb31Launcher.ps1'
$tempRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path
$fixtureDirectory = Join-Path $tempRoot "XB31 Launcher Test $([Guid]::NewGuid().ToString('N'))"

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "FAIL: launcher installer is missing: $installerPath"
}

try {
    $desktopPath = Join-Path $fixtureDirectory 'Desktop'
    $fixtureRepositoryRoot = Join-Path $fixtureDirectory 'Repository With Spaces'
    New-Item -ItemType Directory -Path $desktopPath, $fixtureRepositoryRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fixtureRepositoryRoot 'Shutdown-With-Xb31.ps1') -Value '# fixture' -Encoding ASCII

    $launcherPath = Join-Path $desktopPath 'Shutdown XB31 and Windows.bat'
    $unrelatedPath = Join-Path $desktopPath 'Keep me.txt'
    Set-Content -LiteralPath $launcherPath -Value 'old launcher' -Encoding ASCII
    Set-Content -LiteralPath $unrelatedPath -Value 'unchanged' -Encoding ASCII

    $output = @(
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath `
            -DesktopPath $desktopPath `
            -RepositoryRoot $fixtureRepositoryRoot 2>&1 |
            ForEach-Object { $_.ToString() }
    )
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "FAIL: installer exited with $exitCode. Output: $($output -join ' | ')"
    }

    if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
        throw "FAIL: installer did not create the Desktop launcher: $launcherPath"
    }

    $launcherText = Get-Content -LiteralPath $launcherPath -Raw
    $expectedScriptPath = Join-Path $fixtureRepositoryRoot 'Shutdown-With-Xb31.ps1'
    $expectedInvocation = ('-File "{0}" -ForceShutdown' -f $expectedScriptPath)
    if ($launcherText -notmatch [Regex]::Escape($expectedInvocation)) {
        throw "FAIL: launcher did not quote and invoke the shutdown script. Launcher: $launcherText"
    }

    if ($launcherText -notmatch '(?m)^exit /b %ERRORLEVEL%\s*$') {
        throw "FAIL: launcher does not propagate the PowerShell exit code. Launcher: $launcherText"
    }

    if ((Get-Content -LiteralPath $unrelatedPath -Raw).Trim() -ne 'unchanged') {
        throw 'FAIL: installer modified an unrelated Desktop file'
    }

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $defaultRepositoryOutput = @(
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath `
                -DesktopPath $desktopPath 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $defaultRepositoryExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($defaultRepositoryExitCode -ne 0) {
        throw "FAIL: installer did not default RepositoryRoot to its own directory. Output: $($defaultRepositoryOutput -join ' | ')"
    }

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $missingDesktopOutput = @(
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath `
                -DesktopPath '' `
                -RepositoryRoot $fixtureRepositoryRoot 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $missingDesktopExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($missingDesktopExitCode -eq 0 -or ($missingDesktopOutput -join ' ') -notmatch 'Desktop') {
        throw "FAIL: missing Desktop path was not rejected clearly. Output: $($missingDesktopOutput -join ' | ')"
    }

    Write-Output 'PASS: launcher installer writes only the named Desktop batch with a quoted fail-closed invocation'
}
finally {
    if (Test-Path -LiteralPath $fixtureDirectory) {
        Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force
    }
}
