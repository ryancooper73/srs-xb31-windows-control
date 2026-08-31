$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\Xb31.Control\Xb31.Control.csproj'
$windowPath = Join-Path $repoRoot 'src\Xb31.Control\MainWindow.xaml'
$pngPath = Join-Path $repoRoot 'src\Xb31.Control\Assets\Xb31Control.png'
$iconPath = Join-Path $repoRoot 'src\Xb31.Control\Assets\Xb31Control.ico'
$exePath = Join-Path $repoRoot 'src\Xb31.Control\bin\Release\net10.0-windows10.0.26100.0\Xb31.Control.exe'

foreach ($requiredPath in @($pngPath, $iconPath, $exePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "FAIL: required icon artifact is missing: $requiredPath"
    }
}

Add-Type -AssemblyName System.Drawing
$pixelVerifierSource = @'
using System;
using System.Drawing;

public static class Xb31IconPixelVerifier
{
    public static void VerifyPackagedArtwork(Bitmap packaged)
    {
        int opaquePixels = 0;
        int transparentPixels = 0;
        for (int y = 0; y < packaged.Height; y++)
        {
            for (int x = 0; x < packaged.Width; x++)
            {
                Color actual = packaged.GetPixel(x, y);
                if (actual.A == 0)
                {
                    transparentPixels++;
                    continue;
                }

                if (actual.A != 255)
                    throw new InvalidOperationException("Packaged artwork contains a partially transparent retained pixel.");

                opaquePixels++;
            }
        }

        if (opaquePixels < 1000000)
            throw new InvalidOperationException("Too little of the supplied artwork was retained.");
        if (transparentPixels < 10000)
            throw new InvalidOperationException("The rounded exterior was not made transparent.");
    }

    public static void VerifyEqual(Bitmap expected, Bitmap actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            throw new InvalidOperationException("Embedded executable icon dimensions differ from the ICO asset.");

        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                if (expected.GetPixel(x, y).ToArgb() != actual.GetPixel(x, y).ToArgb())
                    throw new InvalidOperationException("Embedded executable icon pixels differ from the ICO asset.");
            }
        }
    }
}
'@
Add-Type -TypeDefinition $pixelVerifierSource -ReferencedAssemblies 'System.Drawing'

$png = [System.Drawing.Bitmap]::FromFile($pngPath)
try {
    if ($png.Width -ne 1165 -or $png.Height -ne 1165) {
        throw 'FAIL: packaged PNG must use the exact trimmed 1165x1165 canvas'
    }

    [Xb31IconPixelVerifier]::VerifyPackagedArtwork($png)

    foreach ($corner in @(
        @(0, 0),
        @($($png.Width - 1), 0),
        @(0, $($png.Height - 1)),
        @($($png.Width - 1), $($png.Height - 1))
    )) {
        if ($png.GetPixel($corner[0], $corner[1]).A -ne 0) {
            throw 'FAIL: pixels outside the rounded icon silhouette must be transparent'
        }
    }
}
finally {
    $png.Dispose()
}

$iconBytes = [System.IO.File]::ReadAllBytes($iconPath)
if ($iconBytes.Length -lt 6 -or
    $iconBytes[0] -ne 0 -or $iconBytes[1] -ne 0 -or
    $iconBytes[2] -ne 1 -or $iconBytes[3] -ne 0) {
    throw 'FAIL: generated icon must be a Windows ICO file'
}

$imageCount = [BitConverter]::ToUInt16($iconBytes, 4)
$expectedSizes = @(16, 24, 32, 48, 64, 128, 256)
$comparisonFramePayload = $null
if ($imageCount -ne $expectedSizes.Count) {
    throw "FAIL: generated icon must contain $($expectedSizes.Count) image sizes"
}

for ($index = 0; $index -lt $imageCount; $index++) {
    $entryOffset = 6 + ($index * 16)
    $width = if ($iconBytes[$entryOffset] -eq 0) { 256 } else { [int]$iconBytes[$entryOffset] }
    $height = if ($iconBytes[$entryOffset + 1] -eq 0) { 256 } else { [int]$iconBytes[$entryOffset + 1] }
    $planes = [BitConverter]::ToUInt16($iconBytes, $entryOffset + 4)
    $bitsPerPixel = [BitConverter]::ToUInt16($iconBytes, $entryOffset + 6)
    $payloadLength = [BitConverter]::ToUInt32($iconBytes, $entryOffset + 8)
    $payloadOffset = [BitConverter]::ToUInt32($iconBytes, $entryOffset + 12)
    $expectedSize = $expectedSizes[$index]

    if ($width -ne $expectedSize -or $height -ne $expectedSize -or
        $planes -ne 1 -or $bitsPerPixel -ne 32) {
        throw "FAIL: ICO frame $index has invalid dimensions or bit depth"
    }
    if (($payloadOffset + $payloadLength) -gt $iconBytes.Length) {
        throw "FAIL: ICO frame $index points outside the file"
    }

    $payload = [byte[]]::new($payloadLength)
    [Array]::Copy($iconBytes, $payloadOffset, $payload, 0, $payloadLength)
    $pngSignature = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    for ($signatureIndex = 0; $signatureIndex -lt $pngSignature.Length; $signatureIndex++) {
        if ($payload[$signatureIndex] -ne $pngSignature[$signatureIndex]) {
            throw "FAIL: ICO frame $index is not PNG encoded"
        }
    }
    if ($expectedSize -eq 32) {
        $comparisonFramePayload = $payload
    }

    $stream = [System.IO.MemoryStream]::new($payload, $false)
    $frame = [System.Drawing.Bitmap]::FromStream($stream)
    try {
        if ($frame.Width -ne $expectedSize -or $frame.Height -ne $expectedSize) {
            throw "FAIL: decoded ICO frame $index has incorrect dimensions"
        }
    }
    finally {
        $frame.Dispose()
        $stream.Dispose()
    }
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$applicationIcon = $project.Project.PropertyGroup.ApplicationIcon | Select-Object -First 1
if ($applicationIcon -ne 'Assets\Xb31Control.ico') {
    throw 'FAIL: Xb31.Control executable must embed Assets\Xb31Control.ico'
}

[xml]$window = Get-Content -LiteralPath $windowPath -Raw
if ($window.DocumentElement.GetAttribute('Icon') -ne 'Assets/Xb31Control.ico') {
    throw 'FAIL: MainWindow must use the packaged XB31 icon'
}

$associatedIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($exePath)
if ($null -eq $associatedIcon) {
    throw 'FAIL: Release executable must expose an associated Windows icon'
}
if ($null -eq $comparisonFramePayload) {
    throw 'FAIL: ICO did not provide a 32x32 frame for executable comparison'
}
$assetStream = [System.IO.MemoryStream]::new($comparisonFramePayload, $false)
$assetBitmap = [System.Drawing.Bitmap]::FromStream($assetStream)
$associatedBitmap = $associatedIcon.ToBitmap()
try {
    [Xb31IconPixelVerifier]::VerifyEqual($assetBitmap, $associatedBitmap)
}
finally {
    $assetBitmap.Dispose()
    $assetStream.Dispose()
    $associatedBitmap.Dispose()
    $associatedIcon.Dispose()
}

Write-Host 'PASS: XB31 control icon packaging contract'
