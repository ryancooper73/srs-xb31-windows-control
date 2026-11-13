#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$SourcePublishPath,
    [string]$InstallRoot = (Join-Path ([Environment]::GetFolderPath(
        [Environment+SpecialFolder]::LocalApplicationData)) 'XB31 Control\releases'),
    [string]$DesktopPath = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::DesktopDirectory),
    [string]$RepositoryRoot = $PSScriptRoot,
    [string]$RunRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
    [string]$RunValueName = 'XB31 Control'
)

$ErrorActionPreference = 'Stop'
$stagingPath = $null

try {
    if ([string]::IsNullOrWhiteSpace($DesktopPath) -or
        -not (Test-Path -LiteralPath $DesktopPath -PathType Container)) {
        throw "Desktop directory does not exist: $DesktopPath"
    }

    if ([string]::IsNullOrWhiteSpace($RepositoryRoot) -or
        -not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) {
        throw "Repository root does not exist: $RepositoryRoot"
    }

    if ([string]::IsNullOrWhiteSpace($SourcePublishPath)) {
        $stagingPath = Join-Path ([IO.Path]::GetTempPath()) (
            'XB31-Control-Publish-{0}' -f [Guid]::NewGuid().ToString('N'))
        $projectPath = Join-Path $RepositoryRoot 'src\Xb31.Control\Xb31.Control.csproj'
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw "Control project is missing: $projectPath"
        }

        & dotnet publish $projectPath -c Release --no-restore -o $stagingPath
        if ($LASTEXITCODE -ne 0) {
            throw "Control app publish failed with exit code $LASTEXITCODE."
        }

        $SourcePublishPath = $stagingPath
    }

    if (-not (Test-Path -LiteralPath $SourcePublishPath -PathType Container)) {
        throw "Published app directory does not exist: $SourcePublishPath"
    }

    $sourceExecutable = Join-Path $SourcePublishPath 'Xb31.Control.exe'
    if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
        throw "Published control executable is missing: $sourceExecutable"
    }

    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    $releaseDirectory = Join-Path $InstallRoot (
        'XB31-Control-{0}-{1}' -f (Get-Date -Format 'yyyyMMddHHmmss'), [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $releaseDirectory -ErrorAction Stop | Out-Null
    Get-ChildItem -LiteralPath $SourcePublishPath -Force |
        Copy-Item -Destination $releaseDirectory -Recurse -Force

    $installedExecutable = Join-Path $releaseDirectory 'Xb31.Control.exe'
    if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
        throw "Published executable was not copied: $installedExecutable"
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcutPath = Join-Path $DesktopPath 'XB31 Control.lnk'
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installedExecutable
    $shortcut.WorkingDirectory = $releaseDirectory
    $shortcut.Description = 'XB31 Control'
    $shortcut.Save()

    New-Item -Path $RunRegistryPath -Force | Out-Null
    $startupCommand = '"{0}" --startup' -f $installedExecutable
    New-ItemProperty -LiteralPath $RunRegistryPath -Name $RunValueName -Value $startupCommand -PropertyType String -Force | Out-Null

    $obsoleteShortcutPath = Join-Path $DesktopPath 'Shutdown-With-Xb31.ps1 - Raccourci.lnk'
    $obsoleteScriptPath = Join-Path $RepositoryRoot 'Shutdown-With-Xb31.ps1'
    if ((Test-Path -LiteralPath $obsoleteShortcutPath -PathType Leaf) -and
        (Test-Path -LiteralPath $obsoleteScriptPath -PathType Leaf)) {
        $obsoleteShortcut = $shell.CreateShortcut($obsoleteShortcutPath)
        $targetPath = $obsoleteShortcut.TargetPath
        if (-not [string]::IsNullOrWhiteSpace($targetPath) -and
            ((Resolve-Path -LiteralPath $targetPath).Path -ieq (Resolve-Path -LiteralPath $obsoleteScriptPath).Path) -and
            [string]::IsNullOrWhiteSpace($obsoleteShortcut.Arguments)) {
            Remove-Item -LiteralPath $obsoleteShortcutPath -Force
        }
    }

    Write-Output $installedExecutable
}
finally {
    if ($null -ne $stagingPath -and (Test-Path -LiteralPath $stagingPath)) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }
}
