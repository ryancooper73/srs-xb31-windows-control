$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path

$appCodePath = Join-Path $repositoryRoot 'src\Xb31.Control\App.xaml.cs'
$appCode = Get-Content -LiteralPath $appCodePath -Raw
if ($appCode -notmatch 'StartupArguments\.IsStartupLaunch\(e\.Args\)') {
    throw 'FAIL: OnStartup must classify the launch with StartupArguments.IsStartupLaunch'
}

$controllerCodePath = Join-Path $repositoryRoot 'src\Xb31.Control\TrayApplicationController.cs'
$controllerCode = Get-Content -LiteralPath $controllerCodePath -Raw
if ($controllerCode -notmatch 'EnsureHandle\(\)') {
    throw 'FAIL: the hidden startup path must realize the window handle without showing the window'
}

$executablePath = Join-Path $repositoryRoot 'src\Xb31.Control\bin\Release\net10.0-windows10.0.26100.0\Xb31.Control.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "FAIL: Release XB31 control executable is missing: $executablePath"
}

$executablePath = (Resolve-Path -LiteralPath $executablePath).Path
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $executablePath.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "FAIL: XB31 control executable resolved outside the worktree: $executablePath"
}

Add-Type -AssemblyName UIAutomationClient

$process = $null
$activatingProcess = $null

try {
    $process = Start-Process -FilePath $executablePath `
        -ArgumentList '--offline-startup-test', '--startup' -PassThru

    # The window must stay hidden for the whole observation window, not merely at first.
    $hiddenDeadline = [DateTime]::UtcNow.AddSeconds(8)
    while ([DateTime]::UtcNow -lt $hiddenDeadline) {
        if ($process.HasExited) {
            throw "FAIL: the startup-mode XB31 control exited with code $($process.ExitCode)"
        }

        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            throw 'FAIL: the startup-mode XB31 control showed its main window'
        }

        Start-Sleep -Milliseconds 200
    }

    Write-Host 'PASS: --startup kept the main window hidden while the process stayed alive'

    # Reaching the activation listener proves Start() completed, so the tray icon exists
    # and the display-state monitor attached to a real window handle.
    $activatingProcess = Start-Process -FilePath $executablePath `
        -ArgumentList '--offline-startup-test' -PassThru
    if (-not $activatingProcess.WaitForExit(10000)) {
        throw "FAIL: the activating XB31 control process $($activatingProcess.Id) did not exit promptly"
    }

    if ($activatingProcess.ExitCode -ne 0) {
        throw "FAIL: the activating XB31 control process exited with code $($activatingProcess.ExitCode)"
    }

    $window = $null
    $showDeadline = [DateTime]::UtcNow.AddSeconds(20)
    while ($null -eq $window -and [DateTime]::UtcNow -lt $showDeadline) {
        if ($process.HasExited) {
            throw "FAIL: the startup-mode XB31 control exited with code $($process.ExitCode)"
        }

        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }

        if ($null -eq $window) {
            Start-Sleep -Milliseconds 100
        }
    }

    if ($null -eq $window) {
        throw 'FAIL: tray activation did not show the hidden main window'
    }

    if ($window.Current.IsOffscreen) {
        throw 'FAIL: the shown window is offscreen'
    }

    # The view model only initializes once the window is shown for the first time.
    $textCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::Text)
    $terminalStatus = $null
    $statusDeadline = [DateTime]::UtcNow.AddSeconds(20)
    while ($null -eq $terminalStatus -and [DateTime]::UtcNow -lt $statusDeadline) {
        $textElements = $window.FindAll([Windows.Automation.TreeScope]::Descendants, $textCondition)
        for ($index = 0; $index -lt $textElements.Count; $index++) {
            if ($textElements.Item($index).Current.Name -eq 'Speaker unavailable') {
                $terminalStatus = 'Speaker unavailable'
                break
            }
        }

        if ($null -eq $terminalStatus) {
            Start-Sleep -Milliseconds 100
        }
    }

    if ($null -eq $terminalStatus) {
        throw 'FAIL: the window shown from startup mode never reached a terminal status'
    }
}
finally {
    foreach ($launched in @($activatingProcess, $process)) {
        if ($null -ne $launched -and -not $launched.HasExited) {
            $launched.Kill()
            if (-not $launched.WaitForExit(5000)) {
                throw "FAIL: launched XB31 control process $($launched.Id) could not be terminated"
            }
        }
    }
}

Write-Host 'PASS: tray activation showed the hidden window and it initialized normally'
