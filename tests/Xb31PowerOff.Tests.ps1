$ErrorActionPreference = 'Stop'

$projectPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Xb31.PowerOff\Xb31.PowerOff.csproj'
$expectedFrameHex = '3e0000000000053000000f00443c'

$output = @(
    & dotnet run --project $projectPath --configuration Release -- --verify-frame 2>&1 |
        ForEach-Object { $_.ToString() }
)
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    throw "FAIL: C# frame verification exited with $exitCode. Output: $($output -join ' | ')"
}

if ($output.Count -ne 1 -or $output[0] -cne $expectedFrameHex) {
    throw "FAIL: expected only '$expectedFrameHex', got '$($output -join ' | ')'"
}

if ($output[0] -notmatch '^[0-9a-f]+$') {
    throw "FAIL: expected a lowercase hexadecimal frame line, got '$($output[0])'"
}

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = 'dotnet'
$startInfo.Arguments = "run --project `"$projectPath`" --configuration Release -- --unknown"
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$process = New-Object System.Diagnostics.Process
try {
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $invalidOutput = $process.StandardOutput.ReadToEnd().TrimEnd()
    $invalidError = $process.StandardError.ReadToEnd().TrimEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 14) {
        throw "FAIL: invalid arguments exited with $($process.ExitCode). Output: $invalidOutput. Error: $invalidError"
    }

    if ($invalidError -cne 'XB31: invalid arguments') {
        throw "FAIL: expected only 'XB31: invalid arguments' on stderr, got '$invalidError'"
    }
}
finally {
    $process.Dispose()
}

$cleanupHarnessPath = Join-Path ([IO.Path]::GetTempPath()) "Xb31PowerOff-Cleanup-$([Guid]::NewGuid().ToString('N'))"
try {
    [void](New-Item -ItemType Directory -Path $cleanupHarnessPath)
    $programPath = Join-Path (Split-Path -Parent $projectPath) 'Program.cs'
    $escapedProgramPath = [Security.SecurityElement]::Escape($programPath)
    $cleanupProjectPath = Join-Path $cleanupHarnessPath 'CleanupHarness.csproj'
    $fakeCorePath = Join-Path $cleanupHarnessPath 'FakeCore.cs'

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$escapedProgramPath" Link="Program.cs" />
    <Compile Include="FakeCore.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $cleanupProjectPath -Encoding UTF8

    @'
namespace Xb31.Core;

public enum Xb31Status
{
    Success,
    Unavailable,
    ConnectionFailed,
    WriteFailed,
    Timeout,
    MalformedCommand,
    UnexpectedFailure,
    CleanupFailed
}

public sealed record Xb31Result(Xb31Status Status, Exception? Diagnostic = null)
{
    public bool IsSuccess => Status == Xb31Status.Success;
}

public sealed class Xb31Client
{
    public Xb31Client(Action<string>? report = null)
    {
    }

    public Task<Xb31Result> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new Xb31Result(
            Xb31Status.CleanupFailed,
            new IOException("injected cleanup failure")));

    public Task<Xb31Result> PowerOffAsync(CancellationToken cancellationToken = default) =>
        ProbeAsync(cancellationToken);
}

public static class Xb31Commands
{
    public static byte[] PowerOffFrame() =>
        Convert.FromHexString("3e0000000000053000000f00443c");
}
'@ | Set-Content -LiteralPath $fakeCorePath -Encoding UTF8

    $buildStartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $buildStartInfo.FileName = 'dotnet'
    $buildStartInfo.Arguments = "build `"$cleanupProjectPath`" --configuration Release --nologo --verbosity quiet"
    $buildStartInfo.UseShellExecute = $false
    $buildStartInfo.CreateNoWindow = $true
    $buildStartInfo.RedirectStandardOutput = $true
    $buildStartInfo.RedirectStandardError = $true
    $buildProcess = New-Object System.Diagnostics.Process
    try {
        $buildProcess.StartInfo = $buildStartInfo
        [void]$buildProcess.Start()
        $buildOutput = $buildProcess.StandardOutput.ReadToEnd().TrimEnd()
        $buildError = $buildProcess.StandardError.ReadToEnd().TrimEnd()
        $buildProcess.WaitForExit()
        if ($buildProcess.ExitCode -ne 0) {
            throw "FAIL: cleanup CLI harness build exited with $($buildProcess.ExitCode). Output: $buildOutput. Error: $buildError"
        }
    }
    finally {
        $buildProcess.Dispose()
    }

    $cleanupAssemblyPath = Join-Path $cleanupHarnessPath 'bin\Release\net8.0\CleanupHarness.dll'
    $cleanupCases = @(
        [pscustomobject]@{ Arguments = '--probe'; ExpectedOutput = 'XB31: probe complete; no data sent'; Name = 'probe' }
        [pscustomobject]@{ Arguments = ''; ExpectedOutput = 'XB31: power-off frame sent'; Name = 'power-off' }
    )
    foreach ($cleanupCase in $cleanupCases) {
        $cleanupStartInfo = New-Object System.Diagnostics.ProcessStartInfo
        $cleanupStartInfo.FileName = 'dotnet'
        $cleanupStartInfo.Arguments = "`"$cleanupAssemblyPath`" $($cleanupCase.Arguments)".TrimEnd()
        $cleanupStartInfo.UseShellExecute = $false
        $cleanupStartInfo.CreateNoWindow = $true
        $cleanupStartInfo.RedirectStandardOutput = $true
        $cleanupStartInfo.RedirectStandardError = $true
        $cleanupProcess = New-Object System.Diagnostics.Process
        try {
            $cleanupProcess.StartInfo = $cleanupStartInfo
            [void]$cleanupProcess.Start()
            $cleanupOutput = $cleanupProcess.StandardOutput.ReadToEnd().TrimEnd()
            $cleanupError = $cleanupProcess.StandardError.ReadToEnd().TrimEnd()
            $cleanupProcess.WaitForExit()

            if ($cleanupProcess.ExitCode -ne 14) {
                throw "FAIL: $($cleanupCase.Name) cleanup failure exited with $($cleanupProcess.ExitCode). Output: $cleanupOutput. Error: $cleanupError"
            }

            if ($cleanupOutput -cne $cleanupCase.ExpectedOutput) {
                throw "FAIL: cleanup failure omitted the completed $($cleanupCase.Name) message. Output: $cleanupOutput"
            }

            if ($cleanupError -cne 'XB31: cleanup failed') {
                throw "FAIL: expected only 'XB31: cleanup failed' on stderr, got '$cleanupError'"
            }
        }
        finally {
            $cleanupProcess.Dispose()
        }
    }
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedCleanupHarnessPath = [IO.Path]::GetFullPath($cleanupHarnessPath)
    if ($resolvedCleanupHarnessPath.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedCleanupHarnessPath)) {
        Remove-Item -LiteralPath $resolvedCleanupHarnessPath -Recurse -Force
    }
}

Write-Output 'PASS: typed helper exact sequence-zero power-off frame verified'
