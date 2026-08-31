$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$appCodePath = Join-Path $repositoryRoot 'src\Xb31.Control\App.xaml.cs'
$appCode = Get-Content -LiteralPath $appCodePath -Raw
if ($appCode -notmatch 'var\s+client\s*=\s*StartupClientFactory\.Create\(\s*e\.Args' -or
    $appCode -notmatch 'new\s+MainWindow\(client\)') {
    throw 'FAIL: OnStartup must inject the startup client factory result into MainWindow; refusing to launch'
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

$controlAssemblyPath = [IO.Path]::ChangeExtension($executablePath, '.dll')
$coreAssemblyPath = Join-Path $repositoryRoot 'src\Xb31.Core\bin\Release\net8.0-windows10.0.26100.0\Xb31.Core.dll'
foreach ($assemblyPath in @($controlAssemblyPath, $coreAssemblyPath)) {
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "FAIL: startup composition dependency is missing: $assemblyPath"
    }
}

$probeRoot = Join-Path ([IO.Path]::GetTempPath()) ("xb31-startup-composition-$([Guid]::NewGuid())")
[void][IO.Directory]::CreateDirectory($probeRoot)
$probeProjectPath = Join-Path $probeRoot 'StartupCompositionProbe.csproj'
$probeProgramPath = Join-Path $probeRoot 'Program.cs'
$escapedControlAssemblyPath = [Security.SecurityElement]::Escape($controlAssemblyPath)
$escapedCoreAssemblyPath = [Security.SecurityElement]::Escape($coreAssemblyPath)
$probeProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Xb31.Control" HintPath="$escapedControlAssemblyPath" />
    <Reference Include="Xb31.Core" HintPath="$escapedCoreAssemblyPath" />
  </ItemGroup>
</Project>
"@
$probeProgram = @'
using Xb31.Control;

var cases = new (string[] Arguments, string ExpectedType)[]
{
    (new[] { "--offline-startup-test" }, "Xb31.Control.OfflineStartupClient"),
    (Array.Empty<string>(), "Xb31.Core.Xb31Client"),
    (new[] { "--offline-startup-test", "extra" }, "Xb31.Core.Xb31Client")
};

foreach ((string[] arguments, string expectedType) in cases)
{
    string actualType = StartupClientFactory.Create(arguments).GetType().FullName
        ?? throw new InvalidOperationException("Startup client type has no full name.");
    if (!string.Equals(actualType, expectedType, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Arguments [{string.Join(", ", arguments)}] selected {actualType}; expected {expectedType}.");
    }
}

Console.WriteLine("PASS: startup client factory composition contract");
'@

try {
    [IO.File]::WriteAllText($probeProjectPath, $probeProject)
    [IO.File]::WriteAllText($probeProgramPath, $probeProgram)
    $probeOutput = & dotnet run --project $probeProjectPath -c Release --verbosity quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "FAIL: startup client factory composition contract failed:`n$($probeOutput -join [Environment]::NewLine)"
    }

    if ($probeOutput -notcontains 'PASS: startup client factory composition contract') {
        throw "FAIL: startup client factory probe returned unexpected output:`n$($probeOutput -join [Environment]::NewLine)"
    }

    Write-Host 'PASS: startup client factory composition contract'
}
finally {
    $resolvedProbeRoot = [IO.Path]::GetFullPath($probeRoot)
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedProbeRoot.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Directory]::Exists($resolvedProbeRoot)) {
        [IO.Directory]::Delete($resolvedProbeRoot, $true)
    }
}

Add-Type -AssemblyName UIAutomationClient

$expectedStatus = 'Speaker unavailable'
$startupStatuses = @('Connecting', $expectedStatus)
$observedStatuses = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$process = $null
$secondProcess = $null
$terminalStatus = $null
$deadline = [DateTime]::UtcNow.AddSeconds(30)

try {
    $process = Start-Process -FilePath $executablePath -ArgumentList '--offline-startup-test' -PassThru
    $window = $null

    while ($null -eq $window -and [DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            throw "FAIL: XB31 control exited during startup with code $($process.ExitCode)"
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
        throw 'FAIL: XB31 control did not expose a WPF main window within 30 seconds'
    }

    $textCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::Text)

    while ($null -eq $terminalStatus -and [DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            throw "FAIL: XB31 control exited before reaching a terminal status with code $($process.ExitCode)"
        }

        $textElements = $window.FindAll(
            [Windows.Automation.TreeScope]::Descendants,
            $textCondition)

        for ($index = 0; $index -lt $textElements.Count; $index++) {
            $text = $textElements.Item($index).Current.Name
            if ($startupStatuses -contains $text) {
                [void]$observedStatuses.Add($text)
            }

            if ($text -eq $expectedStatus) {
                $terminalStatus = $text
                break
            }
        }

        if ($null -eq $terminalStatus) {
            Start-Sleep -Milliseconds 100
        }
    }

    if ($null -eq $terminalStatus) {
        $observed = @($observedStatuses) -join ', '
        if ($observedStatuses.Count -eq 1 -and $observedStatuses.Contains('Connecting')) {
            throw "FAIL: XB31 control remained only in 'Connecting' for 30 seconds"
        }

        throw "FAIL: XB31 control did not reach a terminal status within 30 seconds; observed: $observed"
    }

    $closeCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty,
        'CloseWindowButton')
    $closeButton = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $closeCondition)
    if ($null -eq $closeButton) { throw 'FAIL: close button is unavailable to UI Automation' }
    $invoke = [Windows.Automation.InvokePattern]$closeButton.GetCurrentPattern(
        [Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()

    $hideDeadline = [DateTime]::UtcNow.AddSeconds(5)
    while (-not $window.Current.IsOffscreen -and [DateTime]::UtcNow -lt $hideDeadline) {
        Start-Sleep -Milliseconds 100
    }

    $process.Refresh()
    if ($process.HasExited) {
        throw 'FAIL: closing the XB31 window terminated the tray process'
    }

    if (-not $window.Current.IsOffscreen) {
        throw 'FAIL: the custom close button did not hide the original window'
    }

    $secondProcess = Start-Process -FilePath $executablePath -ArgumentList '--offline-startup-test' -PassThru
    if (-not $secondProcess.WaitForExit(5000)) {
        throw "FAIL: second offline XB31 control process $($secondProcess.Id) did not exit promptly"
    }

    if ($secondProcess.ExitCode -ne 0) {
        throw "FAIL: second offline XB31 control process $($secondProcess.Id) exited with code $($secondProcess.ExitCode)"
    }

    $windowPattern = [Windows.Automation.WindowPattern]$window.GetCurrentPattern(
        [Windows.Automation.WindowPattern]::Pattern)
    $restoreDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while (($window.Current.IsOffscreen -or
        $windowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized) -and
        [DateTime]::UtcNow -lt $restoreDeadline) {
        if ($process.HasExited) {
            throw "FAIL: original XB31 control process exited with code $($process.ExitCode)"
        }

        Start-Sleep -Milliseconds 100
    }

    if ($window.Current.IsOffscreen) {
        throw 'FAIL: launching a second offline instance did not restore the original window'
    }

    if ($windowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized) {
        throw 'FAIL: the restored original window remained minimized'
    }
}
finally {
    if ($null -ne $secondProcess -and -not $secondProcess.HasExited) {
        $secondProcess.Kill()
        if (-not $secondProcess.WaitForExit(5000)) {
            throw "FAIL: launched second XB31 control process $($secondProcess.Id) could not be terminated"
        }
    }

    if ($null -ne $process -and -not $process.HasExited) {
        $process.Kill()
        if (-not $process.WaitForExit(5000)) {
            throw "FAIL: launched XB31 control process $($process.Id) could not be terminated"
        }
    }
}

if ($null -ne $process -and -not $process.HasExited) {
    throw "FAIL: launched XB31 control process $($process.Id) is still running"
}

if ($null -ne $secondProcess -and -not $secondProcess.HasExited) {
    throw "FAIL: launched second XB31 control process $($secondProcess.Id) is still running"
}

Write-Host "PASS: XB31 control startup reached '$terminalStatus', close hid it, and a second launch restored the original window"
