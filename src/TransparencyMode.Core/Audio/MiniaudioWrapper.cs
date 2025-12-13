// =============================================================================
// MiniaudioWrapper.cs - P/Invoke Definitions for Miniaudio Native Audio Engine
// =============================================================================
// Part of the "Path B: Architecture Pivot" implementation.
// This file defines the interop layer between C# and the native miniaudio.dll
// to achieve sub-15ms latency by bypassing CLR garbage collection in the audio path.
// =============================================================================

using System;
using System.Runtime.InteropServices;

namespace TransparencyMode.Core.Audio
{
    /// <summary>
    /// Miniaudio result codes. MA_SUCCESS = 0 indicates success.
    /// </summary>
    public enum MaResult : int
    {
        MA_SUCCESS = 0,
        MA_ERROR = -1,
        MA_INVALID_ARGS = -2,
        MA_INVALID_OPERATION = -3,
        MA_OUT_OF_MEMORY = -4,
        MA_OUT_OF_RANGE = -5,
        MA_ACCESS_DENIED = -6,
        MA_DOES_NOT_EXIST = -7,
        MA_ALREADY_EXISTS = -8,
        MA_TOO_MANY_OPEN_FILES = -9,
        MA_INVALID_FILE = -10,
        MA_TOO_BIG = -11,
        MA_PATH_TOO_LONG = -12,
        MA_NAME_TOO_LONG = -13,
        MA_NOT_DIRECTORY = -14,
        MA_IS_DIRECTORY = -15,
        MA_DIRECTORY_NOT_EMPTY = -16,
        MA_AT_END = -17,
        MA_NO_SPACE = -18,
        MA_BUSY = -19,
        MA_IO_ERROR = -20,
        MA_INTERRUPT = -21,
        MA_UNAVAILABLE = -22,
        MA_ALREADY_IN_USE = -23,
        MA_BAD_ADDRESS = -24,
        MA_BAD_SEEK = -25,
        MA_BAD_PIPE = -26,
        MA_DEADLOCK = -27,
        MA_TOO_MANY_LINKS = -28,
        MA_NOT_IMPLEMENTED = -29,
        MA_NO_MESSAGE = -30,
        MA_BAD_MESSAGE = -31,
        MA_NO_DATA_AVAILABLE = -32,
        MA_INVALID_DATA = -33,
        MA_TIMEOUT = -34,
        MA_NO_NETWORK = -35,
        MA_NOT_UNIQUE = -36,
        MA_NOT_SOCKET = -37,
        MA_NO_ADDRESS = -38,
        MA_BAD_PROTOCOL = -39,
        MA_PROTOCOL_UNAVAILABLE = -40,
        MA_PROTOCOL_NOT_SUPPORTED = -41,
        MA_PROTOCOL_FAMILY_NOT_SUPPORTED = -42,
        MA_ADDRESS_FAMILY_NOT_SUPPORTED = -43,
        MA_SOCKET_NOT_SUPPORTED = -44,
        MA_CONNECTION_RESET = -45,
        MA_ALREADY_CONNECTED = -46,
        MA_NOT_CONNECTED = -47,
        MA_CONNECTION_REFUSED = -48,
        MA_NO_HOST = -49,
        MA_IN_PROGRESS = -50,
        MA_CANCELLED = -51,
        MA_MEMORY_ALREADY_MAPPED = -52,

        // General miniaudio-specific errors
        MA_FORMAT_NOT_SUPPORTED = -100,
        MA_DEVICE_TYPE_NOT_SUPPORTED = -101,
        MA_SHARE_MODE_NOT_SUPPORTED = -102,
        MA_NO_BACKEND = -103,
        MA_NO_DEVICE = -104,
        MA_API_NOT_FOUND = -105,
        MA_INVALID_DEVICE_CONFIG = -106,
        MA_LOOP = -107,
        MA_BACKEND_NOT_ENABLED = -108,

        // State errors
        MA_DEVICE_NOT_INITIALIZED = -200,
        MA_DEVICE_ALREADY_INITIALIZED = -201,
        MA_DEVICE_NOT_STARTED = -202,
        MA_DEVICE_NOT_STOPPED = -203,

        // Operation errors
        MA_FAILED_TO_INIT_BACKEND = -300,
        MA_FAILED_TO_OPEN_BACKEND_DEVICE = -301,
        MA_FAILED_TO_START_BACKEND_DEVICE = -302,
        MA_FAILED_TO_STOP_BACKEND_DEVICE = -303
    }

    /// <summary>
    /// Audio backends supported by Miniaudio.
    /// We force WASAPI for Windows low-latency operation.
    /// </summary>
    public enum MaBackend : int
    {
        MA_BACKEND_WASAPI = 0,
        MA_BACKEND_DSOUND = 1,
        MA_BACKEND_WINMM = 2,
        MA_BACKEND_COREAUDIO = 3,
        MA_BACKEND_SNDIO = 4,
        MA_BACKEND_AUDIO4 = 5,
        MA_BACKEND_OSS = 6,
        MA_BACKEND_PULSEAUDIO = 7,
        MA_BACKEND_ALSA = 8,
        MA_BACKEND_JACK = 9,
        MA_BACKEND_AAUDIO = 10,
        MA_BACKEND_OPENSL = 11,
        MA_BACKEND_WEBAUDIO = 12,
        MA_BACKEND_CUSTOM = 13,
        MA_BACKEND_NULL = 14
    }

    /// <summary>
    /// Audio sample formats. We use Float32 for maximum quality and compatibility.
    /// </summary>
    public enum MaFormat : int
    {
        MA_FORMAT_UNKNOWN = 0,
        MA_FORMAT_U8 = 1,      // Unsigned 8-bit
        MA_FORMAT_S16 = 2,     // Signed 16-bit
        MA_FORMAT_S24 = 3,     // Signed 24-bit (packed)
        MA_FORMAT_S32 = 4,     // Signed 32-bit
        MA_FORMAT_F32 = 5      // 32-bit floating point (PREFERRED)
    }

    /// <summary>
    /// Device type for enumeration and configuration.
    /// </summary>
    public enum MaDeviceType : int
    {
        MA_DEVICE_TYPE_PLAYBACK = 1,
        MA_DEVICE_TYPE_CAPTURE = 2,
        MA_DEVICE_TYPE_DUPLEX = 3,    // Full-duplex (capture + playback)
        MA_DEVICE_TYPE_LOOPBACK = 4
    }

    /// <summary>
    /// Share mode for WASAPI. Shared mode allows other apps to use audio.
    /// </summary>
    public enum MaShareMode : int
    {
        MA_SHARE_MODE_SHARED = 0,
        MA_SHARE_MODE_EXCLUSIVE = 1
    }

    /// <summary>
    /// Performance profile hint for buffer sizing.
    /// </summary>
    public enum MaPerformanceProfile : int
    {
        MA_PERFORMANCE_PROFILE_LOW_LATENCY = 0,
        MA_PERFORMANCE_PROFILE_CONSERVATIVE = 1
    }

    /// <summary>
    /// Native device info structure returned by enumeration.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NativeDeviceInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Id;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Name;

        public int IsDefault;
        public int SampleRate;
        public int Channels;
    }

    /// <summary>
    /// Engine configuration passed to native initialization.
    /// All low-latency critical settings are exposed here.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NativeEngineConfig
    {
        /// <summary>Windows device ID for input (capture)</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string InputDeviceId;

        /// <summary>Windows device ID for output (playback)</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string OutputDeviceId;

        /// <summary>Sample rate (48000 recommended)</summary>
        public uint SampleRate;

        /// <summary>Number of channels (2 for stereo)</summary>
        public uint Channels;

        /// <summary>Buffer size in frames (128 = ~2.6ms at 48kHz)</summary>
        public uint BufferSizeFrames;

        /// <summary>Sample format (use MA_FORMAT_F32)</summary>
        public MaFormat Format;

        /// <summary>Share mode (use MA_SHARE_MODE_SHARED for transparency)</summary>
        public MaShareMode ShareMode;

        /// <summary>Performance profile (use LOW_LATENCY)</summary>
        public MaPerformanceProfile PerformanceProfile;

        /// <summary>
        /// CRITICAL: Disable Windows sample rate conversion to enable IAudioClient3 low-latency mode.
        /// Set to 1 (true) to bypass the Windows mixer's latency.
        /// </summary>
        public int NoAutoConvertSRC;

        /// <summary>
        /// Enable the built-in asynchronous resampler for clock drift compensation.
        /// Set to 1 (true) for duplex streams with different hardware clocks.
        /// </summary>
        public int EnableResampling;

        /// <summary>Initial volume (0.0 to 1.0)</summary>
        public float Volume;

        /// <summary>
        /// Creates a default low-latency configuration for transparency mode.
        /// </summary>
        public static NativeEngineConfig CreateLowLatency(
            string inputDeviceId,
            string outputDeviceId,
            float volume = 1.0f)
        {
            return new NativeEngineConfig
            {
                InputDeviceId = inputDeviceId ?? string.Empty,
                OutputDeviceId = outputDeviceId ?? string.Empty,
                SampleRate = 48000,
                Channels = 2,
                BufferSizeFrames = 128,  // ~2.6ms at 48kHz - break the 10ms wall!
                Format = MaFormat.MA_FORMAT_F32,
                ShareMode = MaShareMode.MA_SHARE_MODE_SHARED,
                PerformanceProfile = MaPerformanceProfile.MA_PERFORMANCE_PROFILE_LOW_LATENCY,
                NoAutoConvertSRC = 1,    // CRITICAL: Enable IAudioClient3 low-latency path
                EnableResampling = 1,    // Enable native drift compensation
                Volume = volume
            };
        }

        /// <summary>
        /// Creates a conservative configuration with larger buffers for problematic hardware.
        /// </summary>
        public static NativeEngineConfig CreateConservative(
            string inputDeviceId,
            string outputDeviceId,
            float volume = 1.0f)
        {
            return new NativeEngineConfig
            {
                InputDeviceId = inputDeviceId ?? string.Empty,
                OutputDeviceId = outputDeviceId ?? string.Empty,
                SampleRate = 48000,
                Channels = 2,
                BufferSizeFrames = 256,  // ~5.3ms at 48kHz - still under 10ms
                Format = MaFormat.MA_FORMAT_F32,
                ShareMode = MaShareMode.MA_SHARE_MODE_SHARED,
                PerformanceProfile = MaPerformanceProfile.MA_PERFORMANCE_PROFILE_LOW_LATENCY,
                NoAutoConvertSRC = 1,
                EnableResampling = 1,
                Volume = volume
            };
        }
    }

    /// <summary>
    /// Engine status information returned by the native engine.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeEngineStatus
    {
        /// <summary>1 if engine is running, 0 otherwise</summary>
        public int IsRunning;

        /// <summary>Current buffer fill level (0.0 to 1.0)</summary>
        public float BufferFillLevel;

        /// <summary>Actual latency in milliseconds</summary>
        public float ActualLatencyMs;

        /// <summary>Number of buffer underruns since start</summary>
        public uint UnderrunCount;

        /// <summary>Number of buffer overruns since start</summary>
        public uint OverrunCount;

        /// <summary>Current volume level (0.0 to 1.0)</summary>
        public float CurrentVolume;

        /// <summary>Last error code</summary>
        public MaResult LastError;
    }

    /// <summary>
    /// Callback delegate for error notifications from native code.
    /// </summary>
    /// <param name="errorCode">The error code</param>
    /// <param name="message">Error message (null-terminated UTF-16)</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void NativeErrorCallback(MaResult errorCode, string message);

    /// <summary>
    /// Callback delegate for device disconnection notifications.
    /// </summary>
    /// <param name="deviceId">The disconnected device ID</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public delegate void NativeDeviceDisconnectedCallback(string deviceId);

    /// <summary>
    /// Callback delegate for state change notifications.
    /// </summary>
    /// <param name="isRunning">1 if running, 0 if stopped</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void NativeStateChangedCallback(int isRunning);

    /// <summary>
    /// P/Invoke wrapper for the native Miniaudio-based audio engine.
    /// This class provides the low-level interop layer.
    /// </summary>
    public static class MiniaudioWrapper
    {
        private const string DllName = "TransparencyAudio.dll";

        // =============================================================================
        // CORE ENGINE FUNCTIONS
        // =============================================================================

        /// <summary>
        /// Initialize the native audio engine with the specified configuration.
        /// Must be called before Start().
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern MaResult AudioEngine_Initialize(ref NativeEngineConfig config);

        /// <summary>
        /// Start audio streaming. Engine must be initialized first.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern MaResult AudioEngine_Start();

        /// <summary>
        /// Stop audio streaming. Engine remains initialized.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern MaResult AudioEngine_Stop();

        /// <summary>
        /// Uninitialize the audio engine and release all resources.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern MaResult AudioEngine_Uninitialize();

        /// <summary>
        /// Set the output volume (0.0 to 1.0).
        /// Thread-safe, can be called while streaming.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern MaResult AudioEngine_SetVolume(float volume);

        /// <summary>
        /// Get the current volume level.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float AudioEngine_GetVolume();

        /// <summary>
        /// Get the current engine status.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern MaResult AudioEngine_GetStatus(out NativeEngineStatus status);

        /// <summary>
        /// Check if the engine is currently running.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int AudioEngine_IsRunning();

        // =============================================================================
        // CALLBACK REGISTRATION
        // =============================================================================

        /// <summary>
        /// Register callback for error notifications.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AudioEngine_SetErrorCallback(NativeErrorCallback callback);

        /// <summary>
        /// Register callback for device disconnection notifications.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AudioEngine_SetDeviceDisconnectedCallback(NativeDeviceDisconnectedCallback callback);

        /// <summary>
        /// Register callback for state change notifications.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void AudioEngine_SetStateChangedCallback(NativeStateChangedCallback callback);

        // =============================================================================
        // DEVICE ENUMERATION
        // =============================================================================

        /// <summary>
        /// Get the number of available capture (input) devices.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int AudioEngine_GetCaptureDeviceCount();

        /// <summary>
        /// Get the number of available playback (output) devices.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int AudioEngine_GetPlaybackDeviceCount();

        /// <summary>
        /// Get information about a capture device by index.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern MaResult AudioEngine_GetCaptureDeviceInfo(int index, out NativeDeviceInfo info);

        /// <summary>
        /// Get information about a playback device by index.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern MaResult AudioEngine_GetPlaybackDeviceInfo(int index, out NativeDeviceInfo info);

        /// <summary>
        /// Refresh the device list. Call after device changes.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern MaResult AudioEngine_RefreshDevices();

        // =============================================================================
        // DIAGNOSTICS
        // =============================================================================

        /// <summary>
        /// Get the last error message.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr AudioEngine_GetLastErrorMessage();

        /// <summary>
        /// Get a human-readable string for a result code.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AudioEngine_ResultToString(MaResult result);
    }

    // =============================================================================
    // WINDOWS MULTIMEDIA TIMER - Required for sub-10ms scheduling
    // =============================================================================

    /// <summary>
    /// Windows Multimedia Timer API for forcing 1ms timer resolution.
    /// Critical for low-latency audio scheduling.
    /// </summary>
    public static class WinmmTimer
    {
        /// <summary>
        /// Request minimum timer resolution. Call with period=1 for 1ms resolution.
        /// MUST be called at application startup for low-latency audio.
        /// </summary>
        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeBeginPeriod(uint period);

        /// <summary>
        /// Release timer resolution request. Call with same period as timeBeginPeriod.
        /// MUST be called at application shutdown to restore system defaults.
        /// </summary>
        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeEndPeriod(uint period);

        /// <summary>
        /// Get timer capabilities (minimum/maximum period).
        /// </summary>
        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeGetDevCaps(ref TimeCaps timeCaps, uint sizeTimeCaps);

        /// <summary>
        /// Timer capabilities structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct TimeCaps
        {
            public uint wPeriodMin;
            public uint wPeriodMax;
        }

        private static bool _timerStarted = false;

        /// <summary>
        /// Enable high-resolution timer (1ms). Call once at startup.
        /// </summary>
        public static void EnableHighResolutionTimer()
        {
            if (!_timerStarted)
            {
                timeBeginPeriod(1);
                _timerStarted = true;
            }
        }

        /// <summary>
        /// Disable high-resolution timer. Call once at shutdown.
        /// </summary>
        public static void DisableHighResolutionTimer()
        {
            if (_timerStarted)
            {
                timeEndPeriod(1);
                _timerStarted = false;
            }
        }
    }
}
