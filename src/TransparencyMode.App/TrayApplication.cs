using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TransparencyMode.Core;
using TransparencyMode.Core.Audio;
using TransparencyMode.Core.Models;

namespace TransparencyMode.App
{
    public class TrayApplication : ApplicationContext
    {
        private NotifyIcon _trayIcon = null!;
        private ContextMenuStrip _contextMenu = null!;
        private SettingsForm? _settingsForm;
        
        private readonly DeviceManager _deviceManager;
        private readonly NativeAudioEngine _audioEngine;
        private AppSettings _settings;

        private ToolStripMenuItem _toggleMenuItem = null!;
        private ToolStripMenuItem _statusMenuItem = null!;
        private readonly SynchronizationContext _syncContext;

        public TrayApplication()
        {
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _deviceManager = new DeviceManager();
            _audioEngine = new NativeAudioEngine();
            _settings = SettingsManager.Load();

            InitializeTrayIcon();
            InitializeEventHandlers();
            
            // Auto-start (Transparency Mode enabled by default)
            TryAutoStart();
        }

        private void InitializeTrayIcon()
        {
            _contextMenu = new ContextMenuStrip();
            
            _statusMenuItem = new ToolStripMenuItem("Status: Inactive")
            {
                Enabled = false,
                Font = new Font(_contextMenu.Font, FontStyle.Bold)
            };
            
            _toggleMenuItem = new ToolStripMenuItem("Enable Transparency Mode");
            _toggleMenuItem.Click += ToggleMenuItem_Click;

            var settingsMenuItem = new ToolStripMenuItem("Settings...");
            settingsMenuItem.Click += SettingsMenuItem_Click;

            var exitMenuItem = new ToolStripMenuItem("Exit");
            exitMenuItem.Click += ExitMenuItem_Click;

            _contextMenu.Items.Add(_statusMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_toggleMenuItem);
            _contextMenu.Items.Add(settingsMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(exitMenuItem);

            _trayIcon = new NotifyIcon()
            {
                Icon = CreateTrayIcon(false),
                ContextMenuStrip = _contextMenu,
                Text = "Transparency Mode - Inactive",
                Visible = true
            };

            _trayIcon.DoubleClick += TrayIcon_DoubleClick;
        }

        private void InitializeEventHandlers()
        {
            _deviceManager.DeviceRemoved += DeviceManager_DeviceRemoved;
            _deviceManager.DeviceAdded += DeviceManager_DeviceAdded;
            _audioEngine.ErrorOccurred += AudioEngine_ErrorOccurred;
            _audioEngine.DeviceDisconnected += AudioEngine_DeviceDisconnected;
            _audioEngine.IsRunningChanged += (s, isRunning) => _syncContext.Post(_ => UpdateTrayStatus(), null);
        }

        private void TryAutoStart()
        {
            try
            {
                // If no devices saved, try to find defaults
                if (string.IsNullOrEmpty(_settings.LastInputDeviceId) || string.IsNullOrEmpty(_settings.LastOutputDeviceId))
                {
                    var input = _deviceManager.GetInputDevices().FirstOrDefault(d => d.IsDefault);
                    var output = _deviceManager.GetOutputDevices().FirstOrDefault(d => d.IsDefault);
                    
                    if (input != null && output != null)
                    {
                        _settings.LastInputDeviceId = input.Id;
                        _settings.LastOutputDeviceId = output.Id;
                        SettingsManager.Save(_settings);
                    }
                }

                // Validate device IDs exist before starting
                var inputDeviceExists = _deviceManager.GetDeviceById(_settings.LastInputDeviceId!) != null;
                var outputDeviceExists = _deviceManager.GetDeviceById(_settings.LastOutputDeviceId!) != null;

                if (inputDeviceExists && outputDeviceExists)
                {
                    _audioEngine.Volume = _settings.Volume;
                    _audioEngine.BufferMilliseconds = _settings.BufferMilliseconds;
                    _audioEngine.LowLatencyMode = _settings.LowLatencyMode;
                    // Pass device IDs directly to native engine
                    _audioEngine.Start(_settings.LastInputDeviceId!, _settings.LastOutputDeviceId!);
                    
                    // Ensure settings reflect the running state
                    if (!_settings.IsEnabled)
                    {
                        _settings.IsEnabled = true;
                        SettingsManager.Save(_settings);
                    }

                    // UpdateTrayStatus will be called by the event handler
                }
            }
            catch
            {
                // Auto-start failed, user will need to configure manually
                _settings.IsEnabled = false;
                SettingsManager.Save(_settings);
            }
        }

        private void ToggleMenuItem_Click(object? sender, EventArgs e)
        {
            if (_audioEngine.IsRunning)
            {
                _audioEngine.Stop();
                _settings.IsEnabled = false;
                SettingsManager.Save(_settings);
                UpdateTrayStatus();
            }
            else
            {
                OpenSettings();
            }
        }

        private void SettingsMenuItem_Click(object? sender, EventArgs e)
        {
            OpenSettings();
        }

        private void TrayIcon_DoubleClick(object? sender, EventArgs e)
        {
            OpenSettings();
        }

        private void OpenSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_deviceManager, _audioEngine, _settings);
                _settingsForm.FormClosed += (s, args) =>
                {
                    _settings = SettingsManager.Load();
                    UpdateTrayStatus();
                };
            }

            _settingsForm.Show();
            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }

        private void ExitMenuItem_Click(object? sender, EventArgs e)
        {
            _audioEngine.Stop();
            _settings.IsEnabled = false;
            SettingsManager.Save(_settings);
            
            _trayIcon.Visible = false;
            _deviceManager.Dispose();
            _audioEngine.Dispose();
            
            Application.Exit();
        }

        private void DeviceManager_DeviceRemoved(object? sender, string deviceId)
        {
            // Check if removed device was one we were using
            if (_audioEngine.IsRunning && 
                (deviceId == _settings.LastInputDeviceId || deviceId == _settings.LastOutputDeviceId))
            {
                _audioEngine.Stop();
                UpdateTrayStatus();
                
                _trayIcon.ShowBalloonTip(3000, "Device Disconnected", 
                    "Audio device was removed. Transparency Mode paused.", ToolTipIcon.Warning);
            }
        }

        private void DeviceManager_DeviceAdded(object? sender, string deviceId)
        {
            // Try to auto-reconnect if we were previously running
            if (!_audioEngine.IsRunning && _settings.IsEnabled &&
                (deviceId == _settings.LastInputDeviceId || deviceId == _settings.LastOutputDeviceId))
            {
                // Give device time to initialize
                System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
                {
                    _syncContext.Post(_ => TryAutoStart(), null);
                });
            }
        }

        private void AudioEngine_ErrorOccurred(object? sender, AudioEngineErrorEventArgs e)
        {
            _syncContext.Post(_ =>
            {
                _audioEngine.Stop();
                UpdateTrayStatus();
                
                _trayIcon.ShowBalloonTip(5000, "Audio Engine Error", 
                    $"Error: {e.Exception.Message}", ToolTipIcon.Error);
            }, null);
        }

        private void AudioEngine_DeviceDisconnected(object? sender, string deviceId)
        {
            _syncContext.Post(_ =>
            {
                UpdateTrayStatus();
                
                _trayIcon.ShowBalloonTip(3000, "Device Disconnected", 
                    "Audio device stopped responding. Transparency Mode paused.", ToolTipIcon.Warning);
            }, null);
        }

        private void UpdateTrayStatus()
        {
            bool isActive = _audioEngine.IsRunning;

            if (isActive)
            {
                _statusMenuItem.Text = "Status: Active ✓";
                _toggleMenuItem.Text = "Disable Transparency Mode";
                _trayIcon.Icon = CreateTrayIcon(true);
                _trayIcon.Text = "Transparency Mode - Active";
            }
            else
            {
                _statusMenuItem.Text = "Status: Inactive";
                _toggleMenuItem.Text = "Enable Transparency Mode";
                _trayIcon.Icon = CreateTrayIcon(false);
                _trayIcon.Text = "Transparency Mode - Inactive";
            }
        }

        private Icon CreateTrayIcon(bool isActive)
        {
            // Create a simple icon programmatically
            var bitmap = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                var color = isActive ? Color.Green : Color.Gray;
                using (var brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, 2, 2, 12, 12);
                }
            }
            
            return Icon.FromHandle(bitmap.GetHicon());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _trayIcon?.Dispose();
                _contextMenu?.Dispose();
                _settingsForm?.Dispose();
                _deviceManager?.Dispose();
                _audioEngine?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
