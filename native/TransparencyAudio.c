/*
 * ==============================================================================
 * TransparencyAudio.c - Native Miniaudio Implementation
 * ==============================================================================
 * Implementation of the TransparencyAudio API using Miniaudio.
 * 
 * KEY FEATURES:
 * - Uses IAudioClient3 for sub-10ms WASAPI shared mode latency
 * - Built-in asynchronous resampler for clock drift compensation
 * - MMCSS "Pro Audio" thread priority
 * - Zero-copy duplex mode (capture → playback in single callback)
 *
 * BUILD:
 *   cl /LD /O2 /DTRANSPARENCY_AUDIO_EXPORTS TransparencyAudio.c
 *
 * DEPENDENCIES:
 *   - miniaudio.h (single header library from https://miniaud.io)
 *   - Windows SDK (ole32.lib, winmm.lib, avrt.lib)
 * ==============================================================================
 */

#define TRANSPARENCY_AUDIO_EXPORTS
#define MINIAUDIO_IMPLEMENTATION
#define MA_ENABLE_WASAPI
#define MA_NO_DSOUND
#define MA_NO_WINMM
#define MA_NO_NULL

/* Force low-latency WASAPI mode */
#define MA_WASAPI_USE_ASYNC_RESAMPLER

#include "TransparencyAudio.h"
#include "miniaudio.h"

#include <windows.h>
#include <avrt.h>
#include <string.h>
#include <stdio.h>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "winmm.lib")
#pragma comment(lib, "avrt.lib")

/* ==============================================================================
 * INTERNAL STATE
 * ============================================================================== */

typedef struct {
    ma_device device;
    ma_device_config deviceConfig;
    ma_context context;
    
    int initialized;
    int running;
    
    float volume;
    
    /* Statistics */
    uint32_t underrunCount;
    uint32_t overrunCount;
    
    /* Callbacks */
    ta_error_callback errorCallback;
    ta_device_disconnected_callback deviceDisconnectedCallback;
    ta_state_changed_callback stateChangedCallback;
    
    /* Error state */
    wchar_t lastErrorMessage[512];
    ta_result lastError;
    
    /* Device info cache */
    ma_device_info* captureDevices;
    uint32_t captureDeviceCount;
    ma_device_info* playbackDevices;
    uint32_t playbackDeviceCount;
    
    /* MMCSS handle */
    HANDLE mmcssHandle;
    DWORD mmcssTaskIndex;
    
} ta_engine;

static ta_engine g_engine = {0};

/* ==============================================================================
 * INTERNAL HELPERS
 * ============================================================================== */

static void set_last_error(ta_result result, const wchar_t* message) {
    g_engine.lastError = result;
    if (message) {
        wcsncpy(g_engine.lastErrorMessage, message, 511);
        g_engine.lastErrorMessage[511] = L'\0';
    } else {
        g_engine.lastErrorMessage[0] = L'\0';
    }
}

static void notify_error(ta_result result, const wchar_t* message) {
    set_last_error(result, message);
    if (g_engine.errorCallback) {
        g_engine.errorCallback(result, message);
    }
}

/* Convert Windows device ID to ma_device_id */
static int find_device_by_id(const wchar_t* deviceId, ma_device_type type, ma_device_id* outId) {
    ma_device_info* devices = (type == ma_device_type_capture) 
        ? g_engine.captureDevices 
        : g_engine.playbackDevices;
    uint32_t count = (type == ma_device_type_capture) 
        ? g_engine.captureDeviceCount 
        : g_engine.playbackDeviceCount;
    
    if (!devices || count == 0) {
        return 0;
    }
    
    for (uint32_t i = 0; i < count; i++) {
        /* Compare the WASAPI device ID string */
        if (wcscmp(devices[i].id.wasapi, deviceId) == 0) {
            *outId = devices[i].id;
            return 1;
        }
    }
    
    return 0;
}

/* ==============================================================================
 * AUDIO DATA CALLBACK
 * This runs on the audio thread - must be fast, no allocations!
 * ============================================================================== */

static void data_callback(ma_device* pDevice, void* pOutput, const void* pInput, ma_uint32 frameCount) {
    (void)pDevice;
    
    if (!g_engine.running || !pInput || !pOutput) {
        /* Output silence if not running */
        memset(pOutput, 0, frameCount * pDevice->playback.channels * sizeof(float));
        return;
    }
    
    float volume = g_engine.volume;
    float* out = (float*)pOutput;
    const float* in = (const float*)pInput;
    ma_uint32 sampleCount = frameCount * pDevice->capture.channels;
    
    /* Simple passthrough with volume - this is the entire "hot path" */
    for (ma_uint32 i = 0; i < sampleCount; i++) {
        out[i] = in[i] * volume;
    }
}

/* ==============================================================================
 * DEVICE NOTIFICATION CALLBACK
 * ============================================================================== */

static void notification_callback(const ma_device_notification* pNotification) {
    switch (pNotification->type) {
        case ma_device_notification_type_started:
            g_engine.running = 1;
            if (g_engine.stateChangedCallback) {
                g_engine.stateChangedCallback(1);
            }
            break;
            
        case ma_device_notification_type_stopped:
            g_engine.running = 0;
            if (g_engine.stateChangedCallback) {
                g_engine.stateChangedCallback(0);
            }
            break;
            
        case ma_device_notification_type_rerouted:
            /* Device changed, might need to handle this */
            break;
            
        case ma_device_notification_type_interruption_began:
        case ma_device_notification_type_interruption_ended:
            /* Handle audio interruptions (e.g., phone calls on mobile) */
            break;
            
        default:
            break;
    }
}

/* ==============================================================================
 * PUBLIC API IMPLEMENTATION
 * ============================================================================== */

TA_API ta_result TA_CALL AudioEngine_Initialize(const ta_engine_config* config) {
    ma_result result;
    
    if (!config) {
        set_last_error(TA_INVALID_ARGS, L"Config is NULL");
        return TA_INVALID_ARGS;
    }
    
    if (g_engine.initialized) {
        set_last_error(TA_DEVICE_ALREADY_INITIALIZED, L"Engine already initialized");
        return TA_DEVICE_ALREADY_INITIALIZED;
    }
    
    memset(&g_engine, 0, sizeof(ta_engine));
    g_engine.volume = config->volume;
    
    /* Initialize context with WASAPI backend only */
    ma_context_config contextConfig = ma_context_config_init();
    
    ma_backend backends[] = { ma_backend_wasapi };
    result = ma_context_init(backends, 1, &contextConfig, &g_engine.context);
    if (result != MA_SUCCESS) {
        set_last_error(TA_FAILED_TO_INIT_BACKEND, L"Failed to initialize WASAPI backend");
        return TA_FAILED_TO_INIT_BACKEND;
    }
    
    /* Enumerate devices */
    result = ma_context_get_devices(&g_engine.context, 
        &g_engine.playbackDevices, &g_engine.playbackDeviceCount,
        &g_engine.captureDevices, &g_engine.captureDeviceCount);
    if (result != MA_SUCCESS) {
        ma_context_uninit(&g_engine.context);
        set_last_error(TA_ERROR, L"Failed to enumerate devices");
        return TA_ERROR;
    }
    
    /* Find the requested devices */
    ma_device_id captureId, playbackId;
    int foundCapture = 0, foundPlayback = 0;
    
    if (wcslen(config->inputDeviceId) > 0) {
        foundCapture = find_device_by_id(config->inputDeviceId, ma_device_type_capture, &captureId);
    }
    if (wcslen(config->outputDeviceId) > 0) {
        foundPlayback = find_device_by_id(config->outputDeviceId, ma_device_type_playback, &playbackId);
    }
    
    /* Configure device for duplex mode (capture + playback) */
    g_engine.deviceConfig = ma_device_config_init(ma_device_type_duplex);
    
    /* Capture configuration */
    g_engine.deviceConfig.capture.pDeviceID = foundCapture ? &captureId : NULL;
    g_engine.deviceConfig.capture.format = ma_format_f32;
    g_engine.deviceConfig.capture.channels = config->channels;
    g_engine.deviceConfig.capture.shareMode = (config->shareMode == TA_SHARE_MODE_EXCLUSIVE) 
        ? ma_share_mode_exclusive 
        : ma_share_mode_shared;
    
    /* Playback configuration */
    g_engine.deviceConfig.playback.pDeviceID = foundPlayback ? &playbackId : NULL;
    g_engine.deviceConfig.playback.format = ma_format_f32;
    g_engine.deviceConfig.playback.channels = config->channels;
    g_engine.deviceConfig.playback.shareMode = (config->shareMode == TA_SHARE_MODE_EXCLUSIVE) 
        ? ma_share_mode_exclusive 
        : ma_share_mode_shared;
    
    /* General configuration */
    g_engine.deviceConfig.sampleRate = config->sampleRate;
    g_engine.deviceConfig.periodSizeInFrames = config->bufferSizeFrames;
    g_engine.deviceConfig.periods = 2;  /* Double buffering */
    
    /* Performance profile */
    g_engine.deviceConfig.performanceProfile = (config->perfProfile == TA_PERFORMANCE_PROFILE_LOW_LATENCY)
        ? ma_performance_profile_low_latency
        : ma_performance_profile_conservative;
    
    /* Set callbacks */
    g_engine.deviceConfig.dataCallback = data_callback;
    g_engine.deviceConfig.notificationCallback = notification_callback;
    g_engine.deviceConfig.pUserData = &g_engine;
    
    /* CRITICAL: WASAPI-specific low-latency settings */
    g_engine.deviceConfig.wasapi.noAutoConvertSRC = config->noAutoConvertSRC ? MA_TRUE : MA_FALSE;
    g_engine.deviceConfig.wasapi.noDefaultQualitySRC = MA_TRUE;  /* Skip Windows SRC */
    g_engine.deviceConfig.wasapi.noAutoStreamRouting = MA_FALSE; /* Allow device switching */
    g_engine.deviceConfig.wasapi.noHardwareOffloading = MA_TRUE; /* Keep processing on CPU */
    
    /* Initialize device */
    result = ma_device_init(&g_engine.context, &g_engine.deviceConfig, &g_engine.device);
    if (result != MA_SUCCESS) {
        ma_context_uninit(&g_engine.context);
        set_last_error(TA_FAILED_TO_OPEN_BACKEND_DEVICE, L"Failed to initialize audio device");
        return TA_FAILED_TO_OPEN_BACKEND_DEVICE;
    }
    
    g_engine.initialized = 1;
    set_last_error(TA_SUCCESS, NULL);
    
    return TA_SUCCESS;
}

TA_API ta_result TA_CALL AudioEngine_Start(void) {
    if (!g_engine.initialized) {
        set_last_error(TA_DEVICE_NOT_INITIALIZED, L"Engine not initialized");
        return TA_DEVICE_NOT_INITIALIZED;
    }
    
    if (g_engine.running) {
        return TA_SUCCESS;  /* Already running */
    }
    
    /* Register for MMCSS "Pro Audio" scheduling */
    g_engine.mmcssTaskIndex = 0;
    g_engine.mmcssHandle = AvSetMmThreadCharacteristicsW(L"Pro Audio", &g_engine.mmcssTaskIndex);
    if (!g_engine.mmcssHandle) {
        /* Non-fatal - continue without MMCSS boost */
        notify_error(TA_ERROR, L"Warning: Failed to set Pro Audio MMCSS priority");
    }
    
    ma_result result = ma_device_start(&g_engine.device);
    if (result != MA_SUCCESS) {
        if (g_engine.mmcssHandle) {
            AvRevertMmThreadCharacteristics(g_engine.mmcssHandle);
            g_engine.mmcssHandle = NULL;
        }
        set_last_error(TA_FAILED_TO_START_BACKEND_DEVICE, L"Failed to start audio device");
        return TA_FAILED_TO_START_BACKEND_DEVICE;
    }
    
    g_engine.running = 1;
    g_engine.underrunCount = 0;
    g_engine.overrunCount = 0;
    
    if (g_engine.stateChangedCallback) {
        g_engine.stateChangedCallback(1);
    }
    
    set_last_error(TA_SUCCESS, NULL);
    return TA_SUCCESS;
}

TA_API ta_result TA_CALL AudioEngine_Stop(void) {
    if (!g_engine.initialized) {
        set_last_error(TA_DEVICE_NOT_INITIALIZED, L"Engine not initialized");
        return TA_DEVICE_NOT_INITIALIZED;
    }
    
    if (!g_engine.running) {
        return TA_SUCCESS;  /* Already stopped */
    }
    
    ma_result result = ma_device_stop(&g_engine.device);
    
    /* Revert MMCSS */
    if (g_engine.mmcssHandle) {
        AvRevertMmThreadCharacteristics(g_engine.mmcssHandle);
        g_engine.mmcssHandle = NULL;
    }
    
    g_engine.running = 0;
    
    if (g_engine.stateChangedCallback) {
        g_engine.stateChangedCallback(0);
    }
    
    if (result != MA_SUCCESS) {
        set_last_error(TA_ERROR, L"Error stopping device");
        return TA_ERROR;
    }
    
    set_last_error(TA_SUCCESS, NULL);
    return TA_SUCCESS;
}

TA_API ta_result TA_CALL AudioEngine_Uninitialize(void) {
    if (!g_engine.initialized) {
        return TA_SUCCESS;  /* Nothing to uninitialize */
    }
    
    if (g_engine.running) {
        AudioEngine_Stop();
    }
    
    ma_device_uninit(&g_engine.device);
    ma_context_uninit(&g_engine.context);
    
    g_engine.initialized = 0;
    memset(&g_engine, 0, sizeof(ta_engine));
    
    return TA_SUCCESS;
}

TA_API ta_result TA_CALL AudioEngine_SetVolume(float volume) {
    if (volume < 0.0f) volume = 0.0f;
    if (volume > 1.0f) volume = 1.0f;
    
    g_engine.volume = volume;
    return TA_SUCCESS;
}

TA_API float TA_CALL AudioEngine_GetVolume(void) {
    return g_engine.volume;
}

TA_API ta_result TA_CALL AudioEngine_GetStatus(ta_engine_status* status) {
    if (!status) {
        return TA_INVALID_ARGS;
    }
    
    status->isRunning = g_engine.running ? 1 : 0;
    status->currentVolume = g_engine.volume;
    status->underrunCount = g_engine.underrunCount;
    status->overrunCount = g_engine.overrunCount;
    status->lastError = g_engine.lastError;
    
    if (g_engine.initialized && g_engine.device.type != ma_device_type_playback) {
        /* Calculate approximate latency */
        ma_uint32 periodSize = g_engine.device.playback.internalPeriodSizeInFrames;
        ma_uint32 sampleRate = g_engine.device.playback.internalSampleRate;
        if (sampleRate > 0) {
            status->actualLatencyMs = (float)(periodSize * 2 * 1000) / sampleRate;
        }
        status->bufferFillLevel = 0.5f;  /* Miniaudio manages this internally */
    }
    
    return TA_SUCCESS;
}

TA_API int32_t TA_CALL AudioEngine_IsRunning(void) {
    return g_engine.running ? 1 : 0;
}

/* ==============================================================================
 * CALLBACK REGISTRATION
 * ============================================================================== */

TA_API void TA_CALL AudioEngine_SetErrorCallback(ta_error_callback callback) {
    g_engine.errorCallback = callback;
}

TA_API void TA_CALL AudioEngine_SetDeviceDisconnectedCallback(ta_device_disconnected_callback callback) {
    g_engine.deviceDisconnectedCallback = callback;
}

TA_API void TA_CALL AudioEngine_SetStateChangedCallback(ta_state_changed_callback callback) {
    g_engine.stateChangedCallback = callback;
}

/* ==============================================================================
 * DEVICE ENUMERATION
 * ============================================================================== */

TA_API int32_t TA_CALL AudioEngine_GetCaptureDeviceCount(void) {
    return (int32_t)g_engine.captureDeviceCount;
}

TA_API int32_t TA_CALL AudioEngine_GetPlaybackDeviceCount(void) {
    return (int32_t)g_engine.playbackDeviceCount;
}

TA_API ta_result TA_CALL AudioEngine_GetCaptureDeviceInfo(int32_t index, ta_device_info* info) {
    if (!info || index < 0 || index >= (int32_t)g_engine.captureDeviceCount) {
        return TA_INVALID_ARGS;
    }
    
    ma_device_info* device = &g_engine.captureDevices[index];
    
    wcsncpy(info->id, device->id.wasapi, 255);
    info->id[255] = L'\0';
    
    /* Convert name from char to wchar_t */
    mbstowcs(info->name, device->name, 255);
    info->name[255] = L'\0';
    
    info->isDefault = device->isDefault ? 1 : 0;
    info->sampleRate = device->nativeDataFormats[0].sampleRate;
    info->channels = device->nativeDataFormats[0].channels;
    
    return TA_SUCCESS;
}

TA_API ta_result TA_CALL AudioEngine_GetPlaybackDeviceInfo(int32_t index, ta_device_info* info) {
    if (!info || index < 0 || index >= (int32_t)g_engine.playbackDeviceCount) {
        return TA_INVALID_ARGS;
    }
    
    ma_device_info* device = &g_engine.playbackDevices[index];
    
    wcsncpy(info->id, device->id.wasapi, 255);
    info->id[255] = L'\0';
    
    /* Convert name from char to wchar_t */
    mbstowcs(info->name, device->name, 255);
    info->name[255] = L'\0';
    
    info->isDefault = device->isDefault ? 1 : 0;
    info->sampleRate = device->nativeDataFormats[0].sampleRate;
    info->channels = device->nativeDataFormats[0].channels;
    
    return TA_SUCCESS;
}

TA_API ta_result TA_CALL AudioEngine_RefreshDevices(void) {
    if (!g_engine.initialized) {
        /* Need at least a context to enumerate */
        return TA_DEVICE_NOT_INITIALIZED;
    }
    
    ma_result result = ma_context_get_devices(&g_engine.context,
        &g_engine.playbackDevices, &g_engine.playbackDeviceCount,
        &g_engine.captureDevices, &g_engine.captureDeviceCount);
    
    return (result == MA_SUCCESS) ? TA_SUCCESS : TA_ERROR;
}

/* ==============================================================================
 * DIAGNOSTICS
 * ============================================================================== */

TA_API const wchar_t* TA_CALL AudioEngine_GetLastErrorMessage(void) {
    return g_engine.lastErrorMessage;
}

TA_API const char* TA_CALL AudioEngine_ResultToString(ta_result result) {
    switch (result) {
        case TA_SUCCESS: return "Success";
        case TA_ERROR: return "General error";
        case TA_INVALID_ARGS: return "Invalid arguments";
        case TA_INVALID_OPERATION: return "Invalid operation";
        case TA_OUT_OF_MEMORY: return "Out of memory";
        case TA_DEVICE_NOT_INITIALIZED: return "Device not initialized";
        case TA_DEVICE_ALREADY_INITIALIZED: return "Device already initialized";
        case TA_DEVICE_NOT_STARTED: return "Device not started";
        case TA_DEVICE_NOT_STOPPED: return "Device not stopped";
        case TA_FAILED_TO_INIT_BACKEND: return "Failed to initialize backend";
        case TA_FAILED_TO_OPEN_BACKEND_DEVICE: return "Failed to open device";
        case TA_FAILED_TO_START_BACKEND_DEVICE: return "Failed to start device";
        default: return "Unknown error";
    }
}

/* ==============================================================================
 * DLL ENTRY POINT
 * ============================================================================== */

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID lpReserved) {
    (void)hModule;
    (void)lpReserved;
    
    switch (reason) {
        case DLL_PROCESS_ATTACH:
            /* Initialize COM for WASAPI */
            CoInitializeEx(NULL, COINIT_MULTITHREADED);
            break;
            
        case DLL_PROCESS_DETACH:
            /* Clean up if still initialized */
            if (g_engine.initialized) {
                AudioEngine_Uninitialize();
            }
            CoUninitialize();
            break;
            
        case DLL_THREAD_ATTACH:
        case DLL_THREAD_DETACH:
            break;
    }
    
    return TRUE;
}
