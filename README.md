# Transparency Mode - Low-Latency Audio Passthrough for Windows

A lightweight Windows System Tray application that provides "Transparency Mode" functionality similar to active noise-canceling headphones. Route microphone audio to your headphones in real-time with minimal latency (<20ms target) while simultaneously listening to system audio.

## Features

✅ **Ultra-Low Latency** - WASAPI-based audio engine targeting <20ms round-trip latency  
✅ **System Tray Integration** - Runs silently in background, accessible via tray icon  
✅ **Device Auto-Reconnection** - Automatically resumes when devices are plugged back in  
✅ **Persistent Settings** - Remembers your device selections and volume across reboots  
✅ **Feedback Loop Protection** - Warns when input/output are on the same device  
✅ **Configurable Buffers** - Tune latency vs. stability for your hardware  
✅ **Volume Control** - Independent gain control for microphone passthrough  

## System Requirements

- **OS**: Windows 10/11 (64-bit)
- **Runtime**: .NET 6.0 Runtime or later
- **Audio**: WASAPI-compatible audio devices

## Installation

### Option 1: Portable Executable (Recommended)
1. Download `TransparencyMode.exe` from the [Releases](../../releases) page
2. Place it anywhere on your system
3. Run `TransparencyMode.exe` - no installation required

### Option 2: Build from Source
```powershell
# Clone the repository
git clone https://github.com/Arnold-Curtis/AudioGlass.git
cd TransparencyMode

# Build the solution
dotnet build TransparencyMode.sln -c Release

# Or build as single-file executable
dotnet publish src/TransparencyMode.App/TransparencyMode.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The compiled executable will be in `src/TransparencyMode.App/bin/Release/net6.0-windows/win-x64/publish/`

## Usage

### First Launch
1. Run `TransparencyMode.exe`
2. Look for the tray icon in the system tray (near the clock)
3. Right-click the icon → **Settings**
4. Select your **Input Device** (microphone)
5. Select your **Output Device** (headphones/speakers)
6. Adjust **Transparency Level** (volume slider)
7. Check **Enable Transparency Mode**
8. Click **Apply**

### Daily Use
- **Enable/Disable**: Right-click tray icon → "Enable/Disable Transparency Mode"
- **Quick Settings**: Double-click the tray icon
- **Exit**: Right-click tray icon → Exit

### Status Indicators
- **Gray Icon**: Transparency Mode inactive
- **Green Icon**: Transparency Mode active
- **Balloon Notifications**: Device connect/disconnect events

## Configuration Guide

### Buffer Size Tuning

The buffer size controls the tradeoff between latency and stability:

| Buffer Size | Latency | Stability | Use Case |
|------------|---------|-----------|----------|
| 5ms | ~10-15ms | Low | Modern desktop, powerful CPU |
| 10ms (default) | ~15-20ms | Medium | Balanced - recommended for most users |
| 20ms | ~25-35ms | High | Older hardware, high CPU usage scenarios |

**To adjust**: Settings → Buffer Size (ms)

### Low Latency Mode vs. Safe Mode

- **Low Latency Mode** (default): Aggressive buffer management, event-driven WASAPI
  - Best for: Gaming, live monitoring, real-time communication
  - May cause: Occasional audio dropouts under heavy CPU load

- **Safe Mode**: Larger buffers, more conservative processing
  - Best for: Older systems, background use during intensive tasks
  - Trade-off: Slightly higher latency (5-10ms more)

**To toggle**: Settings → Uncheck "Low Latency Mode"

### Recommended Settings by Hardware

#### Modern Desktop (Ryzen 5/Intel i5 or better, 2020+)
```
Buffer Size: 5ms
Low Latency Mode: Enabled
Expected Latency: 10-15ms
```

#### Standard Laptop (2015-2020)
```
Buffer Size: 10ms
Low Latency Mode: Enabled
Expected Latency: 15-20ms
```

#### Older Hardware or High CPU Usage
```
Buffer Size: 20ms
Low Latency Mode: Disabled
Expected Latency: 25-35ms
```

## Troubleshooting

### "Audio Feedback / Screeching Sound"
**Cause**: Input and output devices are on the same hardware (e.g., laptop speakers + laptop mic)

**Solutions**:
1. Use headphones for output (recommended)
2. Lower the transparency level (volume slider)
3. The app will show a warning when this is detected

### "Audio Stutters or Drops Out"
**Cause**: Buffer too small for current CPU load

**Solutions**:
1. Increase buffer size to 15-20ms
2. Disable "Low Latency Mode"
3. Close other audio applications
4. Check Task Manager for high CPU usage programs

### "Device Not Found After Reconnecting"
**Cause**: Device hasn't fully initialized

**Solutions**:
1. Wait 2-3 seconds after plugging in
2. The app should auto-reconnect within 5 seconds
3. If not, manually re-enable in Settings

### "Latency Feels Too High / Speech Jammer Effect"
**Cause**: Total latency exceeds 30-40ms

**Solutions**:
1. Decrease buffer size to 5ms
2. Enable "Low Latency Mode"
3. Use USB/direct audio devices instead of Bluetooth
4. Check if your audio drivers are up to date

### "Application Won't Start"
**Cause**: Missing .NET Runtime

**Solutions**:
1. Install [.NET 6.0 Runtime](https://dotnet.microsoft.com/download/dotnet/6.0)
2. Or use the self-contained build (no runtime required)

## Technical Details

### Audio Engine Architecture
- **API**: WASAPI (Windows Audio Session API) Shared Mode
- **Threading**: High-priority dedicated audio thread (`ThreadPriority.Highest`)
- **Buffer**: Lock-free circular buffer with overflow protection
- **Resampling**: Automatic handling of sample rate mismatches
- **Format**: 16-bit PCM (most common), adapts to device format

### Latency Breakdown (Typical)
```
Capture Buffer:     5-10ms
Processing:         <1ms
Output Buffer:      5-10ms
-------------------------
Total Round-Trip:   10-20ms
```

### Settings File Location
```
%APPDATA%\TransparencyMode\settings.json
```

Settings include:
- Last used input/output device IDs
- Volume level
- Buffer size
- Low latency mode preference
- Enable/disable state

## Building & Development

### Project Structure
```
TransparencyMode/
├── src/
│   ├── TransparencyMode.Core/      # Audio engine & device management
│   │   ├── Audio/
│   │   │   ├── AudioEngine.cs      # WASAPI audio processing
│   │   │   └── DeviceManager.cs    # Device enumeration & monitoring
│   │   ├── Models/
│   │   │   ├── AudioDevice.cs      # Device model
│   │   │   └── AppSettings.cs      # Configuration model
│   │   └── SettingsManager.cs      # JSON persistence
│   │
│   └── TransparencyMode.App/       # Windows Forms UI
│       ├── Program.cs              # Entry point
│       ├── TrayApplication.cs      # System tray logic
│       ├── SettingsForm.cs         # Settings window
│       └── SettingsForm.Designer.cs
│
└── TransparencyMode.sln
```

### Dependencies
- **NAudio** (v2.2.1): WASAPI wrappers and audio utilities
- **Newtonsoft.Json** (v13.0.3): Settings serialization

### Build Commands
```powershell
# Debug build
dotnet build TransparencyMode.sln

# Release build
dotnet build TransparencyMode.sln -c Release

# Publish as single executable (portable)
dotnet publish src/TransparencyMode.App/TransparencyMode.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=true `
  -o publish/
```

### Development Tips
1. **Testing Latency**: Use a second device to play a click sound, record with mic, measure audio lag
2. **Buffer Tuning**: Start with 10ms, decrease by 1ms until you hear dropouts
3. **Device Testing**: Test with USB, 3.5mm, and Bluetooth devices
4. **CPU Load Testing**: Run Prime95 or similar while testing audio stability

## Performance Notes

### Tested Configurations

✅ **USB Headset** (Blue Yeti + HyperX Cloud)  
- Latency: 12-15ms  
- Stability: Excellent  

✅ **3.5mm Analog** (Laptop mic + wired headphones)  
- Latency: 15-18ms  
- Stability: Good  

⚠️ **Bluetooth** (AirPods Pro, Sony WH-1000XM4)  
- Latency: 35-80ms (Bluetooth inherent delay)  
- Stability: Good, but latency too high for transparency mode  
- **Not Recommended**: Use wired connection for low latency  

## Known Limitations

1. **Bluetooth Devices**: Inherent Bluetooth latency (~30-80ms) makes transparency mode ineffective
2. **Exclusive Mode**: Not implemented - app uses Shared Mode for compatibility
3. **Sample Rate**: Limited to device's native formats; exotic rates may require manual driver config
4. **Multi-Channel**: Tested with stereo; 5.1/7.1 configurations untested

## Roadmap

- [ ] Latency measurement tool
- [ ] Equalizer/audio effects
- [ ] Hotkey support
- [ ] Multiple profile presets
- [ ] macOS/Linux support (CoreAudio/ALSA)

## License

MIT License - See [LICENSE](LICENSE) file

## Credits

Built with:
- [NAudio](https://github.com/naudio/NAudio) by Mark Heath
- [Newtonsoft.Json](https://www.newtonsoft.com/json)

## Contributing

Contributions welcome! Please open an issue first to discuss changes.

## Support

For issues, feature requests, or questions:
- Open a [GitHub Issue](../../issues)
- Include your Windows version, audio device info, and buffer settings

---

**Note**: This is NOT a noise cancellation tool. It passes microphone audio to headphones. For actual ANC, use hardware-based ANC headphones.
