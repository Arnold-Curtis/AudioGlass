// =============================================================================
// NativeAudioEngine.cs - High-Level Wrapper for Miniaudio Native Engine
// =============================================================================
// Part of the "Path B: Architecture Pivot" implementation.
// This class provides a managed interface to the native audio engine while
// maintaining compatibility with the existing UI (TrayApplication, SettingsForm).
//
// KEY DESIGN DECISIONS:
// - Audio hot path runs in native code (GC-immune)
// - Callbacks are marshaled to UI thread via SynchronizationContext
// - Device IDs are passed as strings (Windows Core Audio device IDs)
// - Volume changes are thread-safe and immediate
// =============================================================================

using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;

namespace TransparencyMode.Core.Audio
{
    /// <summary>
    /// Native audio engine for transparency mode using Miniaudio.
    /// Achieves sub-15ms latency by running the audio path in unmanaged code,
    /// completely bypassing the CLR garbage collector.
    /// </summary>
    public class NativeAudioEngine : IDisposable
    {
        // =============================================================================
        // PRIVATE FIELDS
        // =============================================================================

        private bool _disposed = false;
        private bool _initialized = false;
        private volatile bool _isRunning = false;

        private float _volume = 1.0f;
        private int _bufferMilliseconds = 5;
        private bool _lowLatencyMode = true;

        // Callback delegates - must be stored to prevent GC collection
        private NativeErrorCallback? _errorCallback;
        private NativeDeviceDisconnectedCallback? _deviceDisconnectedCallback;
        private NativeStateChangedCallback? _stateChangedCallback;

        // Handles to prevent delegate GC
        private GCHandle _errorCallbackHandle;
        private GCHandle _deviceDisconnectedCallbackHandle;
        private GCHandle _stateChangedCallbackHandle;

        // Synchronization context for UI thread marshaling
        private readonly SynchronizationContext? _syncContext;

        // =============================================================================
        // PUBLIC PROPERTIES
        // =============================================================================

        /// <summary>
        /// Gets whether the audio engine is currently streaming.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Gets or sets the output volume (0.0 to 1.0).
        /// Changes take effect immediately without restarting.
        /// </summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0.0f, 1.0f);
                if (_initialized)
                {
                    MiniaudioWrapper.AudioEngine_SetVolume(_volume);
                }
            }
        }

        /// <summary>
        /// Gets or sets the target buffer size in milliseconds.
        /// Changes require restart to take effect.
        /// </summary>
        public int BufferMilliseconds
        {
            get => _bufferMilliseconds;
            set => _bufferMilliseconds = Math.Clamp(value, 1, 100);
        }

        /// <summary>
        /// Gets or sets whether low-latency mode is enabled.
        /// When true, uses 128-frame buffer (~2.6ms).
        /// When false, uses 256-frame buffer (~5.3ms).
        /// Changes require restart to take effect.
        /// </summary>
        public bool LowLatencyMode
        {
            get => _lowLatencyMode;
            set => _lowLatencyMode = value;
        }

        // =============================================================================
        // EVENTS
        // =============================================================================

        /// <summary>
        /// Raised when an error occurs in the audio engine.
        /// </summary>
        public event EventHandler<AudioEngineErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// Raised when a device is disconnected.
        /// </summary>
        public event EventHandler<string>? DeviceDisconnected;

        /// <summary>
        /// Raised when the running state changes.
        /// </summary>
        public event EventHandler<bool>? IsRunningChanged;

        // =============================================================================
        // CONSTRUCTOR / DESTRUCTOR
        // =============================================================================

        /// <summary>
        /// Creates a new instance of the native audio engine.
        /// </summary>
        public NativeAudioEngine()
        {
            // Capture the synchronization context for UI thread marshaling
            _syncContext = SynchronizationContext.Current;

            // Enable high-resolution timer for sub-10ms scheduling
            WinmmTimer.EnableHighResolutionTimer();
        }

        ~NativeAudioEngine()
        {
            Dispose(false);
        }

        // =============================================================================
        // PUBLIC METHODS
        // =============================================================================

        /// <summary>
        /// Start audio streaming with the specified input and output devices.
        /// </summary>
        /// <param name="inputDevice">The capture device (microphone).</param>
        /// <param name="outputDevice">The playback device (headphones).</param>
        public void Start(MMDevice inputDevice, MMDevice outputDevice)
        {
            if (inputDevice == null) throw new ArgumentNullException(nameof(inputDevice));
            if (outputDevice == null) throw new ArgumentNullException(nameof(outputDevice));

            Start(inputDevice.ID, outputDevice.ID);
        }

        /// <summary>
        /// Start audio streaming with the specified device IDs.
        /// </summary>
        /// <param name="inputDeviceId">Windows device ID for capture.</param>
        /// <param name="outputDeviceId">Windows device ID for playback.</param>
        public void Start(string inputDeviceId, string outputDeviceId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(inputDeviceId))
                throw new ArgumentNullException(nameof(inputDeviceId));
            if (string.IsNullOrEmpty(outputDeviceId))
                throw new ArgumentNullException(nameof(outputDeviceId));

            if (_isRunning)
            {
                Stop();
            }

            try
            {
                // Set up callbacks before initialization
                SetupCallbacks();

                // Create configuration based on mode
                var config = _lowLatencyMode
                    ? NativeEngineConfig.CreateLowLatency(inputDeviceId, outputDeviceId, _volume)
                    : NativeEngineConfig.CreateConservative(inputDeviceId, outputDeviceId, _volume);

                // Override buffer size if custom value specified
                if (_bufferMilliseconds > 0 && !_lowLatencyMode)
                {
                    // Calculate frames from milliseconds: frames = (ms / 1000) * sampleRate
                    config.BufferSizeFrames = (uint)((_bufferMilliseconds / 1000.0) * config.SampleRate);
                    // Ensure power of 2 for optimal performance
                    config.BufferSizeFrames = NextPowerOfTwo(config.BufferSizeFrames);
                    // Clamp to reasonable range
                    config.BufferSizeFrames = Math.Clamp(config.BufferSizeFrames, 64, 4096);
                }

                // Initialize the native engine
                var result = MiniaudioWrapper.AudioEngine_Initialize(ref config);
                if (result != MaResult.MA_SUCCESS)
                {
                    throw new AudioEngineException($"Failed to initialize native audio engine: {result}");
                }
                _initialized = true;

                // Start streaming
                result = MiniaudioWrapper.AudioEngine_Start();
                if (result != MaResult.MA_SUCCESS)
                {
                    MiniaudioWrapper.AudioEngine_Uninitialize();
                    _initialized = false;
                    throw new AudioEngineException($"Failed to start native audio engine: {result}");
                }

                _isRunning = true;
                RaiseIsRunningChanged(true);
            }
            catch (DllNotFoundException ex)
            {
                throw new AudioEngineException(
                    "TransparencyAudio.dll not found. Please ensure the native library is in the application directory. " +
                    "See NATIVE_BUILD.md for compilation instructions.", ex);
            }
            catch (Exception ex) when (ex is not AudioEngineException)
            {
                RaiseError(new AudioEngineErrorEventArgs(ex));
                throw new AudioEngineException("Failed to start audio engine", ex);
            }
        }

        /// <summary>
        /// Stop audio streaming.
        /// </summary>
        public void Stop()
        {
            ThrowIfDisposed();

            if (!_isRunning && !_initialized)
                return;

            try
            {
                if (_isRunning)
                {
                    MiniaudioWrapper.AudioEngine_Stop();
                    _isRunning = false;
                }

                if (_initialized)
                {
                    MiniaudioWrapper.AudioEngine_Uninitialize();
                    _initialized = false;
                }

                RaiseIsRunningChanged(false);
            }
            catch (Exception ex)
            {
                RaiseError(new AudioEngineErrorEventArgs(ex));
            }
            finally
            {
                CleanupCallbacks();
            }
        }

        /// <summary>
        /// Get the current engine status including latency and buffer statistics.
        /// </summary>
        public NativeEngineStatus GetStatus()
        {
            ThrowIfDisposed();

            if (!_initialized)
            {
                return new NativeEngineStatus
                {
                    IsRunning = 0,
                    LastError = MaResult.MA_DEVICE_NOT_INITIALIZED
                };
            }

            MiniaudioWrapper.AudioEngine_GetStatus(out var status);
            return status;
        }

        // =============================================================================
        // PRIVATE METHODS - Callback Setup
        // =============================================================================

        private void SetupCallbacks()
        {
            // Create callback delegates
            _errorCallback = OnNativeError;
            _deviceDisconnectedCallback = OnNativeDeviceDisconnected;
            _stateChangedCallback = OnNativeStateChanged;

            // Pin delegates to prevent GC collection while native code holds references
            _errorCallbackHandle = GCHandle.Alloc(_errorCallback);
            _deviceDisconnectedCallbackHandle = GCHandle.Alloc(_deviceDisconnectedCallback);
            _stateChangedCallbackHandle = GCHandle.Alloc(_stateChangedCallback);

            // Register callbacks with native code
            MiniaudioWrapper.AudioEngine_SetErrorCallback(_errorCallback);
            MiniaudioWrapper.AudioEngine_SetDeviceDisconnectedCallback(_deviceDisconnectedCallback);
            MiniaudioWrapper.AudioEngine_SetStateChangedCallback(_stateChangedCallback);
        }

        private void CleanupCallbacks()
        {
            // Clear native callbacks first
            try
            {
                MiniaudioWrapper.AudioEngine_SetErrorCallback(null!);
                MiniaudioWrapper.AudioEngine_SetDeviceDisconnectedCallback(null!);
                MiniaudioWrapper.AudioEngine_SetStateChangedCallback(null!);
            }
            catch { /* Ignore errors during cleanup */ }

            // Release GC handles
            if (_errorCallbackHandle.IsAllocated)
                _errorCallbackHandle.Free();
            if (_deviceDisconnectedCallbackHandle.IsAllocated)
                _deviceDisconnectedCallbackHandle.Free();
            if (_stateChangedCallbackHandle.IsAllocated)
                _stateChangedCallbackHandle.Free();

            _errorCallback = null;
            _deviceDisconnectedCallback = null;
            _stateChangedCallback = null;
        }

        // =============================================================================
        // PRIVATE METHODS - Native Callback Handlers
        // =============================================================================

        private void OnNativeError(MaResult errorCode, string message)
        {
            var args = new AudioEngineErrorEventArgs(
                new AudioEngineException($"Native audio error ({errorCode}): {message}"));

            // Marshal to UI thread if available
            if (_syncContext != null)
            {
                _syncContext.Post(_ => RaiseError(args), null);
            }
            else
            {
                RaiseError(args);
            }
        }

        private void OnNativeDeviceDisconnected(string deviceId)
        {
            _isRunning = false;

            // Marshal to UI thread if available
            if (_syncContext != null)
            {
                _syncContext.Post(_ =>
                {
                    DeviceDisconnected?.Invoke(this, deviceId);
                    RaiseIsRunningChanged(false);
                }, null);
            }
            else
            {
                DeviceDisconnected?.Invoke(this, deviceId);
                RaiseIsRunningChanged(false);
            }
        }

        private void OnNativeStateChanged(int isRunning)
        {
            bool running = isRunning != 0;
            _isRunning = running;

            // Marshal to UI thread if available
            if (_syncContext != null)
            {
                _syncContext.Post(_ => RaiseIsRunningChanged(running), null);
            }
            else
            {
                RaiseIsRunningChanged(running);
            }
        }

        // =============================================================================
        // PRIVATE METHODS - Event Raising
        // =============================================================================

        private void RaiseError(AudioEngineErrorEventArgs args)
        {
            ErrorOccurred?.Invoke(this, args);
        }

        private void RaiseIsRunningChanged(bool isRunning)
        {
            IsRunningChanged?.Invoke(this, isRunning);
        }

        // =============================================================================
        // PRIVATE METHODS - Utilities
        // =============================================================================

        private static uint NextPowerOfTwo(uint v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return v;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NativeAudioEngine));
        }

        // =============================================================================
        // DISPOSE PATTERN
        // =============================================================================

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Stop the engine if running
                try
                {
                    Stop();
                }
                catch { /* Ignore errors during disposal */ }
            }

            // Disable high-resolution timer
            WinmmTimer.DisableHighResolutionTimer();

            _disposed = true;
        }
    }

    // =============================================================================
    // EXCEPTION TYPES
    // =============================================================================

    /// <summary>
    /// Exception thrown when an audio engine operation fails.
    /// </summary>
    public class AudioEngineException : Exception
    {
        public AudioEngineException(string message) : base(message) { }
        public AudioEngineException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Event arguments for audio engine errors.
    /// </summary>
    public class AudioEngineErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }

        public AudioEngineErrorEventArgs(Exception exception)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }
    }
}
