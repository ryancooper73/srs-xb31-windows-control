$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$installerPath = Join-Path $repositoryRoot 'Install-Xb31Control.ps1'
$fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) "XB31 Control Install Test $([Guid]::NewGuid().ToString('N'))"
$runRegistryPath = 'HKCU:\Software\XB31 Control Install Test'
$runValueName = "XB31 Control Test $([Guid]::NewGuid().ToString('N'))"

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "FAIL: control app installer is missing: $installerPath"
}

try {
    $publishPath = Join-Path $fixtureDirectory 'Publish'
    $installRoot = Join-Path $fixtureDirectory 'Releases'
    $desktopPath = Join-Path $fixtureDirectory 'Desktop'
    $fixtureRepositoryRoot = Join-Path $fixtureDirectory 'Repository'
    New-Item -ItemType Directory -Path $publishPath, $desktopPath, $fixtureRepositoryRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $publishPath 'Xb31.Control.exe') -Value 'fixture executable' -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $fixtureRepositoryRoot 'Shutdown-With-Xb31.ps1') -Value '# obsolete fixture' -Encoding ASCII

    $shell = New-Object -ComObject WScript.Shell
    $obsoleteShortcutPath = Join-Path $desktopPath 'Shutdown-With-Xb31.ps1 - Raccourci.lnk'
    $obsoleteShortcut = $shell.CreateShortcut($obsoleteShortcutPath)
    $obsoleteShortcut.TargetPath = Join-Path $fixtureRepositoryRoot 'Shutdown-With-Xb31.ps1'
    $obsoleteShortcut.Save()

    $output = @(
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath `
            -SourcePublishPath $publishPath `
            -InstallRoot $installRoot `
            -DesktopPath $desktopPath `
            -RepositoryRoot $fixtureRepositoryRoot `
            -RunRegistryPath $runRegistryPath `
            -RunValueName $runValueName 2>&1 |
            ForEach-Object { $_.ToString() }
    )
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "FAIL: installer exited with $exitCode. Output: $($output -join ' | ')"
    }

    $installedExecutable = $output | Select-Object -Last 1
    if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
        throw "FAIL: installer did not report a published executable. Output: $($output -join ' | ')"
    }
    if ((Split-Path -Parent (Split-Path -Parent $installedExecutable)) -ne $installRoot) {
        throw "FAIL: installed executable was not placed in one versioned release folder: $installedExecutable"
    }

    $shortcutPath = Join-Path $desktopPath 'XB31 Control.lnk'
    if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
        throw "FAIL: installer did not create the app shortcut: $shortcutPath"
    }
    $shortcut = $shell.CreateShortcut($shortcutPath)
    if ($shortcut.TargetPath -cne $installedExecutable) {
        throw "FAIL: shortcut target differs from published executable. Target: $($shortcut.TargetPath)"
    }

    $runValue = (Get-ItemProperty -LiteralPath $runRegistryPath -Name $runValueName).$runValueName
    $expectedRunValue = '"{0}" --startup' -f $installedExecutable
    if ($runValue -cne $expectedRunValue) {
        throw "FAIL: startup registration is incorrect. Expected: $expectedRunValue; actual: $runValue"
    }

    if (Test-Path -LiteralPath $obsoleteShortcutPath -PathType Leaf) {
        throw 'FAIL: verified obsolete shutdown shortcut was not retired'
    }

    $fakeDotnetPath = Join-Path $fixtureDirectory 'fake-dotnet.ps1'
    $fakeDotnetLogPath = Join-Path $fixtureDirectory 'fake-dotnet-arguments.txt'
    @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
$outputIndex = [Array]::IndexOf($Arguments, '-o')
if ($outputIndex -lt 0) { throw 'missing output directory' }
$outputPath = $Arguments[$outputIndex + 1]
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
Set-Content -LiteralPath (Join-Path $outputPath 'Xb31.Control.exe') -Value 'fixture executable' -Encoding ASCII
Set-Content -LiteralPath $env:XB31_FAKE_DOTNET_LOG -Value ($Arguments -join '|') -Encoding ASCII
'@ | Set-Content -LiteralPath $fakeDotnetPath -Encoding ASCII

    $env:XB31_FAKE_DOTNET_LOG = $fakeDotnetLogPath
    try {
        $defaultOutput = @(
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath `
                -InstallRoot (Join-Path $fixtureDirectory 'DefaultReleases') `
                -DesktopPath $desktopPath `
                -RepositoryRoot $repositoryRoot `
                -RunRegistryPath $runRegistryPath `
                -RunValueName $runValueName `
                -DotnetCommand $fakeDotnetPath 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $defaultExitCode = $LASTEXITCODE
    }
    finally {
        Remove-Item Env:\XB31_FAKE_DOTNET_LOG -ErrorAction SilentlyContinue
    }

    if ($defaultExitCode -ne 0) {
        throw "FAIL: default publish path exited with $defaultExitCode. Output: $($defaultOutput -join ' | ')"
    }
    if ((Get-Content -LiteralPath $fakeDotnetLogPath -Raw) -match '--no-restore') {
        throw 'FAIL: default publish skipped restore on a clean installation path'
    }

    Write-Output 'PASS: control installer publishes a versioned release and registers only that executable'
}
finally {
    Remove-ItemProperty -LiteralPath $runRegistryPath -Name $runValueName -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $runRegistryPath -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
