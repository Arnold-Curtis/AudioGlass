# Native Audio Engine Build Instructions

This document explains how to build the native `TransparencyAudio.dll` required for the Path B low-latency audio implementation.

## Overview

The native audio engine uses [Miniaudio](https://miniaud.io), a single-file C audio library that:
- Runs in unmanaged memory (GC-immune)
- Natively supports IAudioClient3 for sub-10ms WASAPI latency
- Includes built-in asynchronous resampling for clock drift compensation
- Handles MMCSS "Pro Audio" thread priority

## Prerequisites

### Option A: Visual Studio (Recommended)
- Visual Studio 2019 or later with "Desktop development with C++" workload
- Windows 10/11 SDK (10.0.19041.0 or later)

### Option B: MinGW-w64
- MinGW-w64 (GCC 10+ recommended)
- Available from: https://www.mingw-w64.org/

### Option C: LLVM/Clang
- LLVM/Clang for Windows
- Available from: https://releases.llvm.org/

## Step 1: Download Miniaudio

1. Download `miniaudio.h` from the official repository:
   ```
   https://raw.githubusercontent.com/mackron/miniaudio/master/miniaudio.h
   ```

2. Place `miniaudio.h` in the `native/` folder alongside `TransparencyAudio.h` and `TransparencyAudio.c`

Your folder structure should be:
```
native/
├── miniaudio.h              # Downloaded from miniaud.io
├── TransparencyAudio.h      # API header (already included)
└── TransparencyAudio.c      # Implementation (already included)
```

## Step 2: Build the DLL

### Using Visual Studio Developer Command Prompt

1. Open "x64 Native Tools Command Prompt for VS 2022" (or your VS version)

2. Navigate to the `native/` folder:
   ```cmd
   cd C:\PROGRAMMING\Visual Studio Code\TransparencyMode\native
   ```

3. Compile with optimizations:
   ```cmd
   cl /LD /O2 /DTRANSPARENCY_AUDIO_EXPORTS /W3 TransparencyAudio.c /link ole32.lib winmm.lib avrt.lib /OUT:TransparencyAudio.dll
   ```

   Flags explained:
   - `/LD` - Create a DLL
   - `/O2` - Maximum optimization for speed
   - `/DTRANSPARENCY_AUDIO_EXPORTS` - Define export macro
   - `/W3` - Warning level 3

### Using MinGW-w64

```bash
gcc -shared -O2 -DTRANSPARENCY_AUDIO_EXPORTS -o TransparencyAudio.dll TransparencyAudio.c -lole32 -lwinmm -lavrt
```

### Using LLVM/Clang

```bash
clang -shared -O2 -DTRANSPARENCY_AUDIO_EXPORTS -o TransparencyAudio.dll TransparencyAudio.c -lole32 -lwinmm -lavrt
```

## Step 3: Deploy the DLL

Copy `TransparencyAudio.dll` to the application output directory:

```
src/TransparencyMode.App/bin/Debug/net6.0-windows/win-x64/TransparencyAudio.dll
```

Or for Release:
```
src/TransparencyMode.App/bin/Release/net6.0-windows/win-x64/TransparencyAudio.dll
```

**Alternative:** Add a post-build step to your `.csproj`:

```xml
<Target Name="CopyNativeDll" AfterTargets="Build">
  <Copy SourceFiles="$(SolutionDir)native\TransparencyAudio.dll" 
        DestinationFolder="$(OutputPath)" 
        SkipUnchangedFiles="true" />
</Target>
```

## Build Script (Automated)

A PowerShell build script is provided for convenience:

```powershell
# build-native.ps1
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# Find Visual Studio
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath

if (-not $vsPath) {
    Write-Error "Visual Studio with C++ tools not found. Please install 'Desktop development with C++' workload."
    exit 1
}

# Set up environment
$vcvarsall = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
cmd /c "`"$vcvarsall`" && cl /LD /O2 /DTRANSPARENCY_AUDIO_EXPORTS /W3 TransparencyAudio.c /link ole32.lib winmm.lib avrt.lib /OUT:TransparencyAudio.dll"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful: TransparencyAudio.dll" -ForegroundColor Green
    
    # Copy to output directories
    $destinations = @(
        "..\src\TransparencyMode.App\bin\Debug\net6.0-windows\win-x64",
        "..\src\TransparencyMode.App\bin\Release\net6.0-windows\win-x64"
    )
    
    foreach ($dest in $destinations) {
        if (Test-Path $dest) {
            Copy-Item "TransparencyAudio.dll" $dest -Force
            Write-Host "Copied to: $dest" -ForegroundColor Cyan
        }
    }
} else {
    Write-Error "Build failed!"
    exit 1
}
```

## Verification

After building, verify the exports using `dumpbin`:

```cmd
dumpbin /exports TransparencyAudio.dll
```

You should see exports like:
```
    ordinal hint RVA      name
          1    0 00001000 AudioEngine_GetCaptureDeviceCount
          2    1 00001010 AudioEngine_GetPlaybackDeviceCount
          3    2 00001020 AudioEngine_GetStatus
          4    3 00001030 AudioEngine_GetVolume
          5    4 00001040 AudioEngine_Initialize
          6    5 00001050 AudioEngine_IsRunning
          ...
```

## Troubleshooting

### "miniaudio.h not found"
Download from https://miniaud.io and place in the `native/` folder.

### "LINK : fatal error LNK1181: cannot open input file 'avrt.lib'"
Install the Windows SDK or ensure it's in your PATH.

### DllNotFoundException at runtime
Ensure `TransparencyAudio.dll` is in the same folder as the executable.

### Audio crackling persists
1. Check that `noAutoConvertSRC` is set to `1` in the config
2. Try increasing `BufferSizeFrames` from 128 to 256
3. Ensure no other audio apps are running in exclusive mode

## Architecture Notes

The native engine implements:

| Feature | Implementation |
|---------|---------------|
| Low-latency WASAPI | IAudioClient3 via Miniaudio |
| Drift compensation | Built-in async resampler |
| Thread priority | MMCSS "Pro Audio" via avrt.dll |
| Sample format | Float32 for maximum quality |
| Buffer size | 128 frames (~2.6ms @ 48kHz) |
| Share mode | Shared (allows other audio apps) |

## References

- [Miniaudio Documentation](https://miniaud.io/docs/manual/index.html)
- [IAudioClient3 (Low Latency Audio)](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio)
- [MMCSS (Multimedia Class Scheduler)](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service)
