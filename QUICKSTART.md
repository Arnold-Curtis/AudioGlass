# Quick Start Guide

## For Users

### Download & Run
1. Navigate to the `publish/` folder
2. Run `TransparencyMode.App.exe`
3. Find the tray icon in your system tray (near the clock)

### First-Time Setup
1. **Right-click** the tray icon → **Settings**
2. Select your **microphone** from the Input dropdown
3. Select your **headphones/speakers** from the Output dropdown
4. Adjust the **volume slider** to your preference (default 100%)
5. Check **"Enable Transparency Mode"**
6. Click **Apply**

### Daily Use
- **Toggle On/Off**: Right-click tray icon → "Enable/Disable Transparency Mode"
- **Settings**: Double-click the tray icon
- **Exit**: Right-click → Exit

## For Developers

### Build from Source
```powershell
# Clone and navigate to project
cd TransparencyMode

# Restore packages
dotnet restore TransparencyMode.sln

# Build (Debug)
dotnet build TransparencyMode.sln

# Build (Release)
dotnet build TransparencyMode.sln -c Release

# Publish as single-file executable
dotnet publish src/TransparencyMode.App/TransparencyMode.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o publish/
```

### Project Structure
```
TransparencyMode/
├── src/
│   ├── TransparencyMode.Core/      # Audio engine library
│   │   ├── Audio/
│   │   │   ├── AudioEngine.cs      # WASAPI audio processing
│   │   │   └── DeviceManager.cs    # Device enumeration & monitoring
│   │   ├── Models/
│   │   │   ├── AudioDevice.cs
│   │   │   └── AppSettings.cs
│   │   └── SettingsManager.cs
│   │
│   └── TransparencyMode.App/       # Windows Forms UI
│       ├── Program.cs              # Entry point
│       ├── TrayApplication.cs      # System tray implementation
│       └── SettingsForm.cs         # Settings window
│
├── publish/                        # Built executable (after publishing)
├── TransparencyMode.sln
└── README.md
```

### Key Technologies
- **.NET 6.0** (Windows-specific)
- **NAudio 2.2.1** - WASAPI audio API wrapper
- **Windows Forms** - Lightweight UI framework
- **WASAPI Shared Mode** - Low-latency audio streaming

### Running in Development
```powershell
# Run from source (Debug)
dotnet run --project src/TransparencyMode.App/TransparencyMode.App.csproj

# Or use Visual Studio/VS Code
# Open TransparencyMode.sln and press F5
```

### Testing Checklist
- [ ] Test with USB headset
- [ ] Test with 3.5mm analog devices
- [ ] Test device disconnect/reconnect
- [ ] Test buffer sizes (5ms, 10ms, 20ms)
- [ ] Test low latency mode toggle
- [ ] Verify settings persistence across restarts
- [ ] Check feedback loop warning

## Troubleshooting

### Build Errors
**Error**: "IMMNotificationClient not found"
- **Fix**: NAudio package not restored. Run `dotnet restore`

**Error**: "net6.0-windows is out of support"
- **Note**: This is a warning, not an error. Build will succeed.
- **Optional**: Update to `net8.0-windows` in `.csproj` files

### Runtime Issues
**No tray icon visible**
- Check Windows Settings → Taskbar → "Select which icons appear on the taskbar"

**High latency (>30ms)**
- Decrease buffer size to 5ms
- Enable "Low Latency Mode"
- Avoid Bluetooth devices (inherent 30-80ms delay)

**Audio dropouts/crackling**
- Increase buffer size to 15-20ms
- Disable "Low Latency Mode"
- Close other audio applications

## License
MIT License - See main README.md
