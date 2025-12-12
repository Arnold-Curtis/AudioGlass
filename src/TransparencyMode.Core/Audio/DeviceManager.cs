using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using TransparencyMode.Core.Models;

namespace TransparencyMode.Core.Audio
{
    /// <summary>
    /// Manages audio device enumeration and monitoring for device changes
    /// Implements MMNotificationClient for automatic device reconnection
    /// </summary>
    public class DeviceManager : IMMNotificationClient, IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator;
        private bool _disposed;

        public event EventHandler? DevicesChanged;
        public event EventHandler<string>? DeviceRemoved;
        public event EventHandler<string>? DeviceAdded;

        public DeviceManager()
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
        }

        /// <summary>
        /// Gets all available input (capture) devices
        /// </summary>
        public List<AudioDevice> GetInputDevices()
        {
            var devices = new List<AudioDevice>();

            try
            {
                var deviceCollection = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

                foreach (var device in deviceCollection)
                {
                    try
                    {
                        devices.Add(new AudioDevice
                        {
                            Id = device.ID,
                            FriendlyName = device.FriendlyName,
                            IsDefault = device.ID == _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).ID,
                            SampleRate = device.AudioClient.MixFormat.SampleRate,
                            Channels = device.AudioClient.MixFormat.Channels
                        });
                    }
                    catch
                    {
                        // Skip devices that can't be queried
                    }
                }
            }
            catch (Exception)
            {
                // Return empty list on error
            }

            return devices;
        }

        /// <summary>
        /// Gets all available output (render) devices
        /// </summary>
        public List<AudioDevice> GetOutputDevices()
        {
            var devices = new List<AudioDevice>();

            try
            {
                var deviceCollection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                foreach (var device in deviceCollection)
                {
                    try
                    {
                        devices.Add(new AudioDevice
                        {
                            Id = device.ID,
                            FriendlyName = device.FriendlyName,
                            IsDefault = device.ID == _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console).ID,
                            SampleRate = device.AudioClient.MixFormat.SampleRate,
                            Channels = device.AudioClient.MixFormat.Channels
                        });
                    }
                    catch
                    {
                        // Skip devices that can't be queried
                    }
                }
            }
            catch (Exception)
            {
                // Return empty list on error
            }

            return devices;
        }

        /// <summary>
        /// Gets a specific device by ID
        /// </summary>
        public MMDevice? GetDeviceById(string deviceId)
        {
            try
            {
                return _enumerator.GetDevice(deviceId);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Checks if input and output devices are on the same physical device
        /// to prevent feedback loops
        /// </summary>
        public bool AreDevicesOnSameHardware(string inputDeviceId, string outputDeviceId)
        {
            try
            {
                var inputDevice = GetDeviceById(inputDeviceId);
                var outputDevice = GetDeviceById(outputDeviceId);

                if (inputDevice == null || outputDevice == null)
                    return false;

                // Simple heuristic: check if device names share significant text
                var inputName = inputDevice.FriendlyName.ToLower();
                var outputName = outputDevice.FriendlyName.ToLower();

                // Extract common device identifiers
                var commonWords = new[] { "realtek", "conexant", "intel", "laptop", "notebook", "built-in", "internal" };

                foreach (var word in commonWords)
                {
                    if (inputName.Contains(word) && outputName.Contains(word))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the default input device
        /// </summary>
        public AudioDevice? GetDefaultInputDevice()
        {
            return GetInputDevices().FirstOrDefault(d => d.IsDefault);
        }

        /// <summary>
        /// Gets the default output device
        /// </summary>
        public AudioDevice? GetDefaultOutputDevice()
        {
            return GetOutputDevices().FirstOrDefault(d => d.IsDefault);
        }

        #region IMMNotificationClient Implementation

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Active)
            {
                DeviceAdded?.Invoke(this, deviceId);
            }
            else if (newState == DeviceState.NotPresent || newState == DeviceState.Unplugged)
            {
                DeviceRemoved?.Invoke(this, deviceId);
            }
            
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            DeviceAdded?.Invoke(this, pwstrDeviceId);
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void OnDeviceRemoved(string deviceId)
        {
            DeviceRemoved?.Invoke(this, deviceId);
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            // Not needed for our purposes
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _enumerator.UnregisterEndpointNotificationCallback(this);
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
