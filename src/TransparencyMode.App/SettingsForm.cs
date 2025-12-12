using System;
using System.Linq;
using System.Windows.Forms;
using TransparencyMode.Core;
using TransparencyMode.Core.Audio;
using TransparencyMode.Core.Models;

using System.Reflection;

namespace TransparencyMode.App
{
    public partial class SettingsForm : Form
    {
        private readonly DeviceManager _deviceManager;
        private readonly AudioEngine _audioEngine;
        private AppSettings _settings;

        public SettingsForm(DeviceManager deviceManager, AudioEngine audioEngine, AppSettings settings)
        {
            InitializeComponent();
            
            _deviceManager = deviceManager;
            _audioEngine = audioEngine;
            _settings = settings;

            InitializeEventHandlers();
            LoadDevices();
            LoadSettings();

            // Set generic icon
            try
            {
                var entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly != null)
                {
                    this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(entryAssembly.Location);
                }
            }
            catch { }
        }

        private void InitializeEventHandlers()
        {
            trackVolume.ValueChanged += TrackVolume_ValueChanged;
            btnApply.Click += BtnApply_Click;
            btnClose.Click += BtnClose_Click;
            chkEnabled.CheckedChanged += ChkEnabled_CheckedChanged;
            chkLowLatency.CheckedChanged += ChkLowLatency_CheckedChanged;
            cmbInputDevice.SelectedIndexChanged += CmbDevice_SelectedIndexChanged;
            cmbOutputDevice.SelectedIndexChanged += CmbDevice_SelectedIndexChanged;
            this.FormClosing += SettingsForm_FormClosing;
        }

        private void LoadDevices()
        {
            // Load input devices
            var inputDevices = _deviceManager.GetInputDevices();
            cmbInputDevice.Items.Clear();
            foreach (var device in inputDevices)
            {
                cmbInputDevice.Items.Add(device);
            }

            // Load output devices
            var outputDevices = _deviceManager.GetOutputDevices();
            cmbOutputDevice.Items.Clear();
            foreach (var device in outputDevices)
            {
                cmbOutputDevice.Items.Add(device);
            }
        }

        private void LoadSettings()
        {
            // Select saved devices
            if (!string.IsNullOrEmpty(_settings.LastInputDeviceId))
            {
                var inputDevice = cmbInputDevice.Items.Cast<AudioDevice>()
                    .FirstOrDefault(d => d.Id == _settings.LastInputDeviceId);
                if (inputDevice != null)
                {
                    cmbInputDevice.SelectedItem = inputDevice;
                }
            }

            if (!string.IsNullOrEmpty(_settings.LastOutputDeviceId))
            {
                var outputDevice = cmbOutputDevice.Items.Cast<AudioDevice>()
                    .FirstOrDefault(d => d.Id == _settings.LastOutputDeviceId);
                if (outputDevice != null)
                {
                    cmbOutputDevice.SelectedItem = outputDevice;
                }
            }

            // Set default selections if nothing selected
            if (cmbInputDevice.SelectedItem == null && cmbInputDevice.Items.Count > 0)
            {
                var defaultInput = cmbInputDevice.Items.Cast<AudioDevice>()
                    .FirstOrDefault(d => d.IsDefault);
                cmbInputDevice.SelectedItem = defaultInput ?? cmbInputDevice.Items[0];
            }

            if (cmbOutputDevice.SelectedItem == null && cmbOutputDevice.Items.Count > 0)
            {
                var defaultOutput = cmbOutputDevice.Items.Cast<AudioDevice>()
                    .FirstOrDefault(d => d.IsDefault);
                cmbOutputDevice.SelectedItem = defaultOutput ?? cmbOutputDevice.Items[0];
            }

            // Load other settings
            trackVolume.Value = (int)(_settings.Volume * 100);
            // Fix: Checkbox should reflect actual engine state
            chkEnabled.Checked = _audioEngine.IsRunning;
            numBuffer.Value = _settings.BufferMilliseconds;
            chkLowLatency.Checked = _settings.LowLatencyMode;

            UpdateVolumeLabel();
            CheckForFeedbackLoop();
        }

        private void TrackVolume_ValueChanged(object? sender, EventArgs e)
        {
            UpdateVolumeLabel();
            // Apply volume in real-time if transparency mode is active
            if (_audioEngine.IsRunning)
            {
                _audioEngine.Volume = trackVolume.Value / 100f;
            }
        }

        private void UpdateVolumeLabel()
        {
            lblVolumeValue.Text = $"{trackVolume.Value}%";
        }

        private void CmbDevice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            CheckForFeedbackLoop();
        }

        private void CheckForFeedbackLoop()
        {
            if (cmbInputDevice.SelectedItem is AudioDevice inputDevice &&
                cmbOutputDevice.SelectedItem is AudioDevice outputDevice)
            {
                if (_deviceManager.AreDevicesOnSameHardware(inputDevice.Id, outputDevice.Id))
                {
                    lblWarning.Text = "⚠ Warning: Input and Output appear to be on the same device. " +
                                    "This may cause audio feedback (screeching). Use headphones.";
                    lblWarning.Visible = true;
                }
                else
                {
                    lblWarning.Text = string.Empty;
                    lblWarning.Visible = false;
                }
            }
        }

        private void ChkEnabled_CheckedChanged(object? sender, EventArgs e)
        {
            if (chkEnabled.Checked)
            {
                ApplySettings();
            }
            else
            {
                _audioEngine.Stop();
                lblStatus.Text = "Transparency Mode Disabled";
                lblStatus.ForeColor = System.Drawing.Color.Gray;
                
                // Ensure we save the disabled state
                _settings.IsEnabled = false;
                SettingsManager.Save(_settings);
            }
        }

        private void ChkLowLatency_CheckedChanged(object? sender, EventArgs e)
        {
            if (chkLowLatency.Checked)
            {
                MessageBox.Show("Low Latency Mode is experimental and may cause audio artifacts (crackling) on some devices.\n\nIf you hear distortion, please disable this mode.", 
                                "Experimental Feature", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BtnApply_Click(object? sender, EventArgs e)
        {
            ApplySettings();
        }

        private void ApplySettings()
        {
            try
            {
                var inputDevice = cmbInputDevice.SelectedItem as AudioDevice;
                var outputDevice = cmbOutputDevice.SelectedItem as AudioDevice;

                if (inputDevice == null || outputDevice == null)
                {
                    MessageBox.Show("Please select both input and output devices.", 
                        "Configuration Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Update settings
                _settings.LastInputDeviceId = inputDevice.Id;
                _settings.LastOutputDeviceId = outputDevice.Id;
                _settings.Volume = trackVolume.Value / 100f;
                _settings.IsEnabled = chkEnabled.Checked;
                _settings.BufferMilliseconds = (int)numBuffer.Value;
                _settings.LowLatencyMode = chkLowLatency.Checked;

                // Apply to audio engine
                _audioEngine.Volume = _settings.Volume;
                _audioEngine.BufferMilliseconds = _settings.BufferMilliseconds;
                _audioEngine.LowLatencyMode = _settings.LowLatencyMode;

                if (chkEnabled.Checked)
                {
                    var mmInputDevice = _deviceManager.GetDeviceById(inputDevice.Id);
                    var mmOutputDevice = _deviceManager.GetDeviceById(outputDevice.Id);

                    if (mmInputDevice != null && mmOutputDevice != null)
                    {
                        _audioEngine.Stop();
                        _audioEngine.Start(mmInputDevice, mmOutputDevice);
                        
                        lblStatus.Text = "✓ Transparency Mode Active";
                        lblStatus.ForeColor = System.Drawing.Color.Green;
                    }
                }

                // Save settings
                SettingsManager.Save(_settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting transparency mode: {ex.Message}", 
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                
                chkEnabled.Checked = false;
                lblStatus.Text = "❌ Error - Check device compatibility";
                lblStatus.ForeColor = System.Drawing.Color.Red;
            }
        }

        private void BtnClose_Click(object? sender, EventArgs e)
        {
            this.Hide();
        }

        private void SettingsForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        }
    }
}
