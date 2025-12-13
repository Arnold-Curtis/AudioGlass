# build-native.ps1
# Build script for TransparencyAudio.dll native library
# 
# Usage:
#   .\build-native.ps1              # Build Release
#   .\build-native.ps1 -Debug       # Build Debug
#   .\build-native.ps1 -DownloadMiniaudio  # Download miniaudio.h first

param(
    [switch]$Debug,
    [switch]$DownloadMiniaudio,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Push-Location $scriptDir
try {
    # Clean if requested
    if ($Clean) {
        Write-Host "Cleaning build artifacts..." -ForegroundColor Yellow
        Remove-Item -Force -ErrorAction SilentlyContinue *.dll, *.obj, *.lib, *.exp, *.pdb
        Write-Host "Clean complete." -ForegroundColor Green
        if (-not $DownloadMiniaudio) {
            exit 0
        }
    }

    # Download miniaudio if requested or missing
    $miniaudioPath = Join-Path $scriptDir "miniaudio.h"
    if ($DownloadMiniaudio -or -not (Test-Path $miniaudioPath)) {
        Write-Host "Downloading miniaudio.h from GitHub..." -ForegroundColor Cyan
        $url = "https://raw.githubusercontent.com/mackron/miniaudio/master/miniaudio.h"
        try {
            Invoke-WebRequest -Uri $url -OutFile $miniaudioPath -UseBasicParsing
            Write-Host "Downloaded miniaudio.h successfully." -ForegroundColor Green
        }
        catch {
            Write-Error "Failed to download miniaudio.h. Please download manually from https://miniaud.io"
            exit 1
        }
    }

    # Verify required files exist
    $requiredFiles = @("miniaudio.h", "TransparencyAudio.h", "TransparencyAudio.c")
    foreach ($file in $requiredFiles) {
        if (-not (Test-Path (Join-Path $scriptDir $file))) {
            Write-Error "Missing required file: $file"
            exit 1
        }
    }

    # Find Visual Studio
    $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vsWhere)) {
        Write-Error "Visual Studio Installer not found. Please install Visual Studio with C++ tools."
        exit 1
    }

    $vsPath = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vsPath) {
        Write-Error "Visual Studio with C++ tools not found. Please install 'Desktop development with C++' workload."
        exit 1
    }

    Write-Host "Found Visual Studio at: $vsPath" -ForegroundColor Cyan

    # Build configuration
    $optimization = if ($Debug) { "/Od /Zi" } else { "/O2" }
    $debugFlag = if ($Debug) { "/DEBUG" } else { "" }
    $configName = if ($Debug) { "Debug" } else { "Release" }

    Write-Host "Building TransparencyAudio.dll ($configName)..." -ForegroundColor Yellow

    # Set up environment and compile
    $vcvarsall = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
    $compileCmd = "cl /LD $optimization /DTRANSPARENCY_AUDIO_EXPORTS /W3 /WX- TransparencyAudio.c /link ole32.lib winmm.lib avrt.lib $debugFlag /OUT:TransparencyAudio.dll"
    
    # Run compilation
    $result = cmd /c "`"$vcvarsall`" >nul 2>&1 && $compileCmd 2>&1"
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        Write-Host $result -ForegroundColor Red
        Write-Error "Build failed with exit code $exitCode"
        exit $exitCode
    }

    Write-Host "Build successful!" -ForegroundColor Green

    # Verify the DLL was created
    $dllPath = Join-Path $scriptDir "TransparencyAudio.dll"
    if (-not (Test-Path $dllPath)) {
        Write-Error "DLL was not created. Check build output for errors."
        exit 1
    }

    $dllInfo = Get-Item $dllPath
    Write-Host "Created: TransparencyAudio.dll ($([math]::Round($dllInfo.Length / 1KB)) KB)" -ForegroundColor Green

    # Copy to output directories
    $appDir = Join-Path $scriptDir "..\src\TransparencyMode.App"
    $destinations = @(
        (Join-Path $appDir "bin\Debug\net6.0-windows\win-x64"),
        (Join-Path $appDir "bin\Release\net6.0-windows\win-x64"),
        (Join-Path $appDir "bin\Debug\net6.0-windows"),
        (Join-Path $appDir "bin\Release\net6.0-windows")
    )

    $copiedCount = 0
    foreach ($dest in $destinations) {
        if (Test-Path $dest) {
            Copy-Item $dllPath $dest -Force
            Write-Host "  Copied to: $dest" -ForegroundColor Cyan
            $copiedCount++
        }
    }

    if ($copiedCount -eq 0) {
        Write-Host "Note: No output directories found. Build the C# project first, then re-run this script." -ForegroundColor Yellow
        Write-Host "  Or manually copy TransparencyAudio.dll to the application output folder." -ForegroundColor Yellow
    }

    Write-Host "`nBuild complete! TransparencyAudio.dll is ready." -ForegroundColor Green
}
finally {
    Pop-Location
}
