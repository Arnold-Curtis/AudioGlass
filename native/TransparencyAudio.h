/*
 * ==============================================================================
 * TransparencyAudio.h - Native Miniaudio Wrapper for C# Interop ("Bare Metal")
 * ==============================================================================
 * This header defines the C API exported by TransparencyAudio.dll.
 * The implementation uses Miniaudio (https://miniaud.io) internally.
 *
 * "BARE METAL" ARCHITECTURE (December 2025):
 * - Decoupled capture/playback devices with elastic ring buffer
 * - Manual clock drift compensation (skip/duplicate frames)
 * - IAudioClient3 for sub-10ms WASAPI shared mode
 * - Variable callback size support (noFixedSizedCallback)
 * - Target latency: ~3-5ms (down from ~100ms)
 *
 * BUILD REQUIREMENTS:
 * - Windows 10/11 SDK
 * - C compiler (MSVC, MinGW, or Clang)
 * - Define TRANSPARENCY_AUDIO_EXPORTS when building the DLL
 *
 * COMPILE COMMAND (MSVC):
 *   cl /LD /O2 /DTRANSPARENCY_AUDIO_EXPORTS TransparencyAudio.c
 *
 * COMPILE COMMAND (MinGW):
 *   gcc -shared -O2 -DTRANSPARENCY_AUDIO_EXPORTS -o TransparencyAudio.dll TransparencyAudio.c -lole32 -lwinmm
 * ==============================================================================
 */

#ifndef TRANSPARENCY_AUDIO_H
#define TRANSPARENCY_AUDIO_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

/* DLL export/import macros */
#ifdef _WIN32
    #ifdef TRANSPARENCY_AUDIO_EXPORTS
        #define TA_API __declspec(dllexport)
    #else
        #define TA_API __declspec(dllimport)
    #endif
    #define TA_CALL __cdecl
#else
    #define TA_API
    #define TA_CALL
#endif

/* ==============================================================================
 * RESULT CODES
 * Matches MaResult enum in MiniaudioWrapper.cs
 * ============================================================================== */

typedef int ta_result;

#define TA_SUCCESS                          0
#define TA_ERROR                           -1
#define TA_INVALID_ARGS                    -2
#define TA_INVALID_OPERATION               -3
#define TA_OUT_OF_MEMORY                   -4
#define TA_DEVICE_NOT_INITIALIZED        -200
#define TA_DEVICE_ALREADY_INITIALIZED    -201
#define TA_DEVICE_NOT_STARTED            -202
#define TA_DEVICE_NOT_STOPPED            -203
#define TA_FAILED_TO_INIT_BACKEND        -300
#define TA_FAILED_TO_OPEN_BACKEND_DEVICE -301
#define TA_FAILED_TO_START_BACKEND_DEVICE -302

/* ==============================================================================
 * ENUMERATIONS
 * ============================================================================== */

typedef enum {
    TA_FORMAT_UNKNOWN = 0,
    TA_FORMAT_U8      = 1,
    TA_FORMAT_S16     = 2,
    TA_FORMAT_S24     = 3,
    TA_FORMAT_S32     = 4,
    TA_FORMAT_F32     = 5
} ta_format;

typedef enum {
    TA_SHARE_MODE_SHARED    = 0,
    TA_SHARE_MODE_EXCLUSIVE = 1
} ta_share_mode;

typedef enum {
    TA_PERFORMANCE_PROFILE_LOW_LATENCY   = 0,
    TA_PERFORMANCE_PROFILE_CONSERVATIVE  = 1
} ta_performance_profile;

/* ==============================================================================
 * STRUCTURES
 * Must match layout in MiniaudioWrapper.cs (LayoutKind.Sequential)
 * ============================================================================== */

/**
 * Device information structure.
 * Returned by device enumeration functions.
 */
typedef struct {
    wchar_t id[256];        /* Windows device ID */
    wchar_t name[256];      /* Friendly name */
    int32_t isDefault;      /* 1 if default device */
    int32_t sampleRate;     /* Native sample rate */
    int32_t channels;       /* Channel count */
} ta_device_info;

/**
 * Engine configuration structure.
 * Passed to AudioEngine_Initialize.
 * 
 * NOTE: New fields added at END for ABI compatibility with existing C# P/Invoke.
 */
typedef struct {
    wchar_t inputDeviceId[256];     /* Capture device ID */
    wchar_t outputDeviceId[256];    /* Playback device ID */
    uint32_t sampleRate;            /* Sample rate (48000 recommended) */
    uint32_t channels;              /* Channel count (2 for stereo) */
    uint32_t bufferSizeFrames;      /* Buffer size in frames (128 = ~2.6ms @ 48kHz) */
    ta_format format;               /* Sample format (TA_FORMAT_F32) */
    ta_share_mode shareMode;        /* WASAPI share mode */
    ta_performance_profile perfProfile; /* Performance profile */
    int32_t noAutoConvertSRC;       /* 1 = disable Windows SRC for low latency */
    int32_t enableResampling;       /* DEPRECATED: Ignored in "Bare Metal" mode */
    float volume;                   /* Initial volume (0.0 - 1.0) */
    
    /* === NEW FIELDS FOR "BARE METAL" ARCHITECTURE === */
    uint32_t ringBufferSizeFrames;  /* Elastic buffer size (0 = use default 2048) */
    int32_t noFixedSizedCallback;   /* 1 = enable variable callback (default: 1) */
    int32_t useDecoupledDevices;    /* 1 = use separate capture/playback (default: 1) */
} ta_engine_config;

/**
 * Engine status structure.
 * Returned by AudioEngine_GetStatus.
 * 
 * NOTE: New fields added at END for ABI compatibility with existing C# P/Invoke.
 */
typedef struct {
    int32_t isRunning;          /* 1 if running */
    float bufferFillLevel;      /* Buffer fill (0.0 - 1.0) */
    float actualLatencyMs;      /* Total round-trip latency in ms */
    uint32_t underrunCount;     /* Buffer underruns since start */
    uint32_t overrunCount;      /* Buffer overruns since start */
    float currentVolume;        /* Current volume */
    ta_result lastError;        /* Last error code */
    
    /* === NEW FIELDS FOR "BARE METAL" ARCHITECTURE === */
    uint32_t driftCorrectionCount;  /* Times drift compensation triggered */
    float ringBufferFillLevel;      /* Elastic buffer fill (0.0 - 1.0) */
    float captureLatencyMs;         /* Capture device latency */
    float playbackLatencyMs;        /* Playback device latency */
} ta_engine_status;

/* ==============================================================================
 * CALLBACK TYPES
 * ============================================================================== */

/**
 * Error callback.
 * Called when an error occurs in the audio thread.
 */
typedef void (TA_CALL *ta_error_callback)(ta_result errorCode, const wchar_t* message);

/**
 * Device disconnected callback.
 * Called when the input or output device is disconnected.
 */
typedef void (TA_CALL *ta_device_disconnected_callback)(const wchar_t* deviceId);

/**
 * State changed callback.
 * Called when the engine starts or stops.
 */
typedef void (TA_CALL *ta_state_changed_callback)(int32_t isRunning);

/* ==============================================================================
 * CORE ENGINE API
 * ============================================================================== */

/**
 * Initialize the audio engine with the specified configuration.
 * Must be called before AudioEngine_Start().
 *
 * @param config Pointer to engine configuration.
 * @return TA_SUCCESS on success, error code otherwise.
 */
TA_API ta_result TA_CALL AudioEngine_Initialize(const ta_engine_config* config);

/**
 * Start audio streaming.
 * Engine must be initialized first.
 *
 * @return TA_SUCCESS on success, error code otherwise.
 */
TA_API ta_result TA_CALL AudioEngine_Start(void);

/**
 * Stop audio streaming.
 * Engine remains initialized and can be restarted.
 *
 * @return TA_SUCCESS on success, error code otherwise.
 */
TA_API ta_result TA_CALL AudioEngine_Stop(void);

/**
 * Uninitialize the audio engine and release all resources.
 *
 * @return TA_SUCCESS on success, error code otherwise.
 */
TA_API ta_result TA_CALL AudioEngine_Uninitialize(void);

/**
 * Set the output volume.
 * Thread-safe, can be called while streaming.
 *
 * @param volume Volume level (0.0 to 1.0).
 * @return TA_SUCCESS on success, error code otherwise.
 */
TA_API ta_result TA_CALL AudioEngine_SetVolume(float volume);

/**
 * Get the current volume level.
 *
 * @return Current volume (0.0 to 1.0).
 */
TA_API float TA_CALL AudioEngine_GetVolume(void);

/**
 * Get the current engine status.
 *
 * @param status Pointer to status structure to fill.
 * @return TA_SUCCESS on success, error code otherwise.
 */
TA_API ta_result TA_CALL AudioEngine_GetStatus(ta_engine_status* status);

/**
 * Check if the engine is currently running.
 *
 * @return 1 if running, 0 otherwise.
 */
TA_API int32_t TA_CALL AudioEngine_IsRunning(void);

/* ==============================================================================
 * CALLBACK REGISTRATION
 * ============================================================================== */

/**
 * Register error callback.
 * Set to NULL to unregister.
 */
TA_API void TA_CALL AudioEngine_SetErrorCallback(ta_error_callback callback);

/**
 * Register device disconnected callback.
 * Set to NULL to unregister.
 */
TA_API void TA_CALL AudioEngine_SetDeviceDisconnectedCallback(ta_device_disconnected_callback callback);

/**
 * Register state changed callback.
 * Set to NULL to unregister.
 */
TA_API void TA_CALL AudioEngine_SetStateChangedCallback(ta_state_changed_callback callback);

/* ==============================================================================
 * DEVICE ENUMERATION
 * ============================================================================== */

/**
 * Get the number of capture (input) devices.
 *
 * @return Number of capture devices.
 */
TA_API int32_t TA_CALL AudioEngine_GetCaptureDeviceCount(void);

/**
 * Get the number of playback (output) devices.
 *
 * @return Number of playback devices.
 */
TA_API int32_t TA_CALL AudioEngine_GetPlaybackDeviceCount(void);

/**
 * Get information about a capture device.
 *
 * @param index Device index (0 to count-1).
 * @param info Pointer to device info structure to fill.
 * @return TA_SUCCESS on success, error code otherwise.
 */
TA_API ta_result TA_CALL AudioEngine_GetCaptureDeviceInfo(int32_t index, ta_device_info* info);

/**
 * Get information about a playback device.
 *
 * @param index Device index (0 to count-1).
 * @param info Pointer to device info structure to fill.
 * @return TA_SUCCESS on success, error code otherwise.
 */
TA_API ta_result TA_CALL AudioEngine_GetPlaybackDeviceInfo(int32_t index, ta_device_info* info);

/**
 * Refresh the device list.
 * Call after device changes.
 *
 * @return TA_SUCCESS on success, error code otherwise.
 */
TA_API ta_result TA_CALL AudioEngine_RefreshDevices(void);

/* ==============================================================================
 * DIAGNOSTICS
 * ============================================================================== */

/**
 * Get the last error message.
 *
 * @return Pointer to error message string (UTF-16). Do not free.
 */
TA_API const wchar_t* TA_CALL AudioEngine_GetLastErrorMessage(void);

/**
 * Get a human-readable string for a result code.
 *
 * @param result The result code.
 * @return Pointer to result string. Do not free.
 */
TA_API const char* TA_CALL AudioEngine_ResultToString(ta_result result);

#ifdef __cplusplus
}
#endif

#endif /* TRANSPARENCY_AUDIO_H */
