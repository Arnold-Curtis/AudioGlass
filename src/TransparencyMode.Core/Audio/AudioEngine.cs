using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TransparencyMode.Core.Audio
{
    /// <summary>
    /// Low-latency WASAPI audio engine for transparency mode.
    /// Implements IWaveProvider to feed the render device directly.
    /// Uses a lock-free circular buffer and event-driven architecture.
    /// </summary>
    public class AudioEngine : IDisposable, IWaveProvider
    {
        private WasapiCapture? _capture;
        private WasapiOut? _renderer;
        private LockFreeCircularBuffer? _circularBuffer;
        private MMDevice? _inputDevice;
        private MMDevice? _outputDevice;
        
        // Buffers for processing
        private float[]? _conversionBuffer; // For Input Bytes -> Float
        private float[]? _downsampleBuffer; // For Downsampling
        private float[]? _resampleBuffer;   // For Resampling
        
        private volatile bool _isRunning;
        private volatile float _volume = 1.0f;
        private int _bufferMilliseconds = 10;
        private bool _lowLatencyMode = true;
        private int _internalSampleRate = 48000; // The rate stored in circular buffer

        // Resampling State
        private double _resampleFraction = 0.0;
        private double _integralError = 0.0; // For PI Controller
        private bool _isPreBuffering = true; // Start in pre-buffering mode
        
        // Cubic Interpolation History (y0, y1, y2, y3)
        // We need to store the last 3 samples from the previous buffer to maintain continuity.
        private float[] _inputHistory = new float[4];

        // Writer Delegate
        private Action<byte[], int, float>? _sampleWriter;

        // MMCSS State
        private bool _mmcssSetCapture = false;
        private bool _mmcssSetRender = false;
        private IntPtr _mmcssHandleCapture = IntPtr.Zero;
        private IntPtr _mmcssHandleRender = IntPtr.Zero;

        public event EventHandler<Exception>? ErrorOccurred;
        public event EventHandler? DeviceDisconnected;
        public event EventHandler<bool>? IsRunningChanged;

        public WaveFormat WaveFormat { get; private set; }

        public bool IsRunning => _isRunning;
        
        public float Volume 
        { 
            get => _volume;
            set => _volume = Math.Clamp(value, 0f, 1.0f);
        }

        public int BufferMilliseconds
        {
            get => _bufferMilliseconds;
            set => _bufferMilliseconds = Math.Clamp(value, 5, 100);
        }

        public bool LowLatencyMode
        {
            get => _lowLatencyMode;
            set => _lowLatencyMode = value;
        }

        private void Log(string message)
        {
            try
            {
                string logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TransparencyMode");
                if (!System.IO.Directory.Exists(logDir))
                {
                    System.IO.Directory.CreateDirectory(logDir);
                }
                System.IO.File.AppendAllText(System.IO.Path.Combine(logDir, "debug.log"), $"{DateTime.Now:HH:mm:ss.fff}: {message}{Environment.NewLine}");
            }
            catch { }
        }

        public AudioEngine()
        {
            Log("AudioEngine initialized");
            // Default format until initialized
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        }

        /// <summary>
        /// Starts the audio passthrough engine
        /// </summary>
        public void Start(MMDevice inputDevice, MMDevice outputDevice)
        {
            Log($"Start called. Input: {inputDevice.FriendlyName}, Output: {outputDevice.FriendlyName}");
            if (_isRunning) Stop();

            try
            {
                _inputDevice = inputDevice;
                _outputDevice = outputDevice;

                // 0. Get Output Format first to guide Input if possible
                WaveFormat = _outputDevice.AudioClient.MixFormat;
                Log($"Output Format: {WaveFormat}");

                // Request Hardware Minimums
                if (_lowLatencyMode)
                {
                    try
                    {
                        // AudioClient.DevicePeriod returns value in 100-nanosecond units
                        // 1 ms = 10,000 units
                        long minPeriod = _outputDevice.AudioClient.MinimumDevicePeriod;
                        int minPeriodMs = (int)(minPeriod / 10000);
                        
                        Log($"Device Min Period: {minPeriodMs}ms");
                        
                        // Use minimum period but ensure it's at least 10ms to be safe
                        // 3ms was too aggressive and caused driver starvation (crackles)
                        _bufferMilliseconds = Math.Max(minPeriodMs, 10);
                    }
                    catch (Exception ex)
                    {
                        Log($"Error querying device period: {ex.Message}");
                        // Fallback to default if query fails
                        _bufferMilliseconds = 20;
                    }
                }
                else
                {
                    // Standard Mode: Use a safe buffer size (e.g. 60ms) to prevent dropouts
                    _bufferMilliseconds = 60;
                }
                
                Log($"Using Buffer Size: {_bufferMilliseconds}ms");

                // 1. Initialize Capture (Event Driven)
                // UseEventSync = true puts WASAPI in Event Driven mode
                _capture = new WasapiCapture(_inputDevice, true, _bufferMilliseconds);
                Log($"Capture initialized. Format: {_capture.WaveFormat}");
                
                // Sample Rate Sanity Check
                if (_capture.WaveFormat.SampleRate != WaveFormat.SampleRate)
                {
                     Log($"WARNING: Sample Rate Mismatch! Input: {_capture.WaveFormat.SampleRate}, Output: {WaveFormat.SampleRate}. Pitch correction via resampling will be active.");
                }

                // Determine Internal Processing Rate
                int inputSampleRate = _capture.WaveFormat.SampleRate;
                if (_lowLatencyMode && inputSampleRate > 48000)
                {
                    // Downsample high-res inputs to 48kHz to save bandwidth/CPU
                    _internalSampleRate = 48000;
                    Log($"High-Res Input Detected ({inputSampleRate}Hz). Downsampling to {_internalSampleRate}Hz for stability.");
                }
                else
                {
                    _internalSampleRate = inputSampleRate;
                }

                // 2. Setup Circular Buffer
                // Calculate required size based on input format
                // We want at least 500ms of buffer
                int inputChannels = _capture.WaveFormat.Channels;
                int samplesPerSecond = _internalSampleRate * inputChannels;
                int targetCapacity = samplesPerSecond / 2; // 500ms
                
                // Find next power of 2
                int capacity = 65536;
                while (capacity < targetCapacity) capacity *= 2;
                
                _circularBuffer = new LockFreeCircularBuffer(capacity);
                Log($"Circular Buffer Capacity: {capacity} samples");
                
                // 3. Determine Output Format (Already done at step 0)
                // WaveFormat is already set.

                // Initialize Sample Writer
                InitializeSampleWriter();
                
                // Reset Control Loop State
                _integralError = 0.0;
                Array.Clear(_inputHistory, 0, _inputHistory.Length);
                
                // 4. Pre-allocate processing buffers
                // Allocate enough for 200ms of the highest bandwidth format to be safe
                int outSampleRate = WaveFormat.SampleRate;
                int outChannels = WaveFormat.Channels;
                
                int maxRate = Math.Max(inputSampleRate, outSampleRate);
                int maxChannels = Math.Max(inputChannels, outChannels);
                
                int maxSamples = (int)(maxRate * maxChannels * 0.2); // 200ms
                // Ensure minimum size
                maxSamples = Math.Max(maxSamples, 32768);
                
                _conversionBuffer = new float[maxSamples];
                _downsampleBuffer = new float[maxSamples];
                _resampleBuffer = new float[maxSamples];
                Log($"Processing Buffers: {maxSamples} samples");
                
                // Reset State
                _resampleFraction = 0.0;
                _isPreBuffering = true;
                _mmcssSetCapture = false;
                _mmcssSetRender = false;

                // 5. Initialize Renderer (Event Driven)
                // We pass 'this' as the IWaveProvider
                _renderer = new WasapiOut(_outputDevice, AudioClientShareMode.Shared, true, _bufferMilliseconds);
                _renderer.Init(this);
                Log("Renderer initialized");

                // 6. Hook Events
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _isRunning = true;
                
                // Start
                _capture.StartRecording();
                _renderer.Play();
                Log("Playback started");
                IsRunningChanged?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                Log($"Error in Start: {ex.Message}\n{ex.StackTrace}");
                _isRunning = false;
                ErrorOccurred?.Invoke(this, ex);
                IsRunningChanged?.Invoke(this, false);
                Cleanup();
                throw;
            }
        }

        /// <summary>
        /// Stops the audio passthrough engine
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            Cleanup();
            IsRunningChanged?.Invoke(this, false);
        }

        private int _logCounter = 0;

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRunning || _circularBuffer == null || _conversionBuffer == null) return;

            try
            {
                // Set Thread Priority to Pro Audio once
                if (!_mmcssSetCapture)
                {
                    try
                    {
                        uint taskIndex = 0;
                        _mmcssHandleCapture = Mmcss.AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
                        Log("MMCSS Capture Priority Set");
                    }
                    catch (Exception ex) { Log($"MMCSS Capture Failed: {ex.Message}"); }
                    _mmcssSetCapture = true;
                }

                // 1. Convert Bytes to Float
                int bytesRecorded = e.BytesRecorded;
                int samplesRead = 0;
                var format = _capture!.WaveFormat;

                // Debug logging for the first few frames
                if (_logCounter < 5)
                {
                    Log($"OnDataAvailable: Bytes={bytesRecorded}, Encoding={format.Encoding}, Bits={format.BitsPerSample}, Channels={format.Channels}");
                    if (format is WaveFormatExtensible ext)
                    {
                        Log($"  Extensible SubFormat: {ext.SubFormat}");
                    }
                    _logCounter++;
                }

                bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
                bool isPcm = format.Encoding == WaveFormatEncoding.Pcm;
                bool isExtensible = format.Encoding == WaveFormatEncoding.Extensible;

                if (isExtensible && format is WaveFormatExtensible extFormat)
                {
                    // Standard GUIDs for Audio Media Subtypes
                    Guid subTypeFloat = new Guid("00000003-0000-0010-8000-00aa00389b71");
                    Guid subTypePcm = new Guid("00000001-0000-0010-8000-00aa00389b71");

                    if (extFormat.SubFormat == subTypeFloat) isFloat = true;
                    else if (extFormat.SubFormat == subTypePcm) isPcm = true;
                }

                // Handle 32-bit Float (Standard or Extensible)
                if (isFloat && format.BitsPerSample == 32)
                {
                    samplesRead = bytesRecorded / 4;
                    for (int i = 0; i < samplesRead; i++)
                    {
                        float sample = BitConverter.ToSingle(e.Buffer, i * 4);
                        // Apply Volume and Safety Gain (-3dB)
                        _conversionBuffer[i] = sample * _volume * 0.70710678f;
                    }
                }
                // Handle 16-bit PCM (Standard or Extensible)
                else if (isPcm && format.BitsPerSample == 16)
                {
                    samplesRead = bytesRecorded / 2;
                    for (int i = 0; i < samplesRead; i++)
                    {
                        short sample = BitConverter.ToInt16(e.Buffer, i * 2);
                        float normalized = sample / 32768.0f;
                        _conversionBuffer[i] = normalized * _volume * 0.70710678f;
                    }
                }
                // Handle 24-bit PCM (Standard or Extensible)
                else if (isPcm && format.BitsPerSample == 24)
                {
                    samplesRead = bytesRecorded / 3;
                    for (int i = 0; i < samplesRead; i++)
                    {
                        // 24-bit is 3 bytes, little endian. 3rd byte is MSB (signed)
                        int sampleVal = (e.Buffer[i * 3] | (e.Buffer[i * 3 + 1] << 8) | ((sbyte)e.Buffer[i * 3 + 2] << 16));
                        float normalized = sampleVal / 8388608.0f;
                        _conversionBuffer[i] = normalized * _volume * 0.70710678f;
                    }
                }
                // Handle 32-bit PCM (Standard or Extensible)
                else if (isPcm && format.BitsPerSample == 32)
                {
                    samplesRead = bytesRecorded / 4;
                    for (int i = 0; i < samplesRead; i++)
                    {
                        int sampleVal = BitConverter.ToInt32(e.Buffer, i * 4);
                        float normalized = sampleVal / 2147483648.0f;
                        _conversionBuffer[i] = normalized * _volume * 0.70710678f;
                    }
                }
                else
                {
                    if (_logCounter < 10)
                    {
                        Log($"Unsupported format in loop: {format.Encoding} {format.BitsPerSample} bits");
                        _logCounter++;
                    }
                }

                // 2. Write to Circular Buffer
                if (samplesRead > 0)
                {
                    // Downsample if needed
                    if (_internalSampleRate < format.SampleRate && _downsampleBuffer != null)
                    {
                        int factor = format.SampleRate / _internalSampleRate;
                        // Only support integer factors for simplicity (e.g. 192->48 = 4, 96->48 = 2)
                        // If non-integer, we might have drift, but let's assume standard rates.
                        // If factor is 1 (e.g. 44.1 -> 48 check failed), just copy.
                        
                        if (factor > 1)
                        {
                            int outCount = samplesRead / factor;
                            for (int i = 0; i < outCount; i++)
                            {
                                // Simple boxcar average for anti-aliasing
                                float sum = 0;
                                for (int j = 0; j < factor; j++)
                                {
                                    sum += _conversionBuffer[i * factor + j];
                                }
                                _downsampleBuffer[i] = sum / factor;
                            }
                            _circularBuffer.Write(new ReadOnlySpan<float>(_downsampleBuffer, 0, outCount));
                        }
                        else
                        {
                            // Fallback for non-integer or 1:1
                            _circularBuffer.Write(new ReadOnlySpan<float>(_conversionBuffer, 0, samplesRead));
                        }
                    }
                    else
                    {
                        _circularBuffer.Write(new ReadOnlySpan<float>(_conversionBuffer, 0, samplesRead));
                    }
                }
            }
            catch (Exception ex)
            {
                // Don't throw on audio thread, just log/notify
                Log($"Error in OnDataAvailable: {ex.Message}");
                _isRunning = false;
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        /// <summary>
        /// Render Callback: Called by WasapiOut (IWaveProvider.Read)
        /// Runs on NAudio's Render Thread
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (!_isRunning || _circularBuffer == null || _resampleBuffer == null || _sampleWriter == null)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            try
            {
                // Set Thread Priority to Pro Audio once
                if (!_mmcssSetRender)
                {
                    try
                    {
                        // Ensure the thread is running at highest priority for the scheduler
                        Thread.CurrentThread.Priority = ThreadPriority.Highest;
                        
                        uint taskIndex = 0;
                        _mmcssHandleRender = Mmcss.AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
                        Log("MMCSS Render Priority Set (Pro Audio + Highest)");
                    }
                    catch (Exception ex) { Log($"MMCSS Render Failed: {ex.Message}"); }
                    _mmcssSetRender = true;
                }

                int bytesPerSample = WaveFormat.BitsPerSample / 8;
                int outSamples = count / bytesPerSample;
                
                // Calculate Base Ratio
                double inRate = _internalSampleRate;
                double outRate = WaveFormat.SampleRate;
                double ratio = inRate / outRate;

                // --- Target Latency & Pre-Buffering Logic ---
                int available = _circularBuffer.Available;
                int targetSamples;
                
                if (_lowLatencyMode)
                {
                    // Target: 15ms (Low Latency)
                    targetSamples = (int)(inRate * 0.015); 
                }
                else
                {
                    // Target: 60ms (Standard Mode - Safe)
                    targetSamples = (int)(inRate * 0.060);
                }

                // Pre-Buffering / Starvation Recovery
                // If we are pre-buffering, wait until we have a safety margin (Target + 10ms)
                // For Normal Mode, we want a larger cushion (e.g. 20ms extra)
                int safetyMargin = _lowLatencyMode ? (int)(inRate * 0.010) : (int)(inRate * 0.020);
                int preBufferThreshold = targetSamples + safetyMargin;

                if (_isPreBuffering)
                {
                    if (available < preBufferThreshold)
                    {
                        // Still buffering - output silence
                        Array.Clear(buffer, offset, count);
                        return count;
                    }
                    else
                    {
                        // Buffer full enough - start playing
                        _isPreBuffering = false;
                        _integralError = 0; // Reset PI controller
                        // Log("Pre-Buffering Complete. Starting Playback."); 
                    }
                }
                
                // Starvation Check
                // If we run dry, go back to pre-buffering to avoid "helicopter" pulsing
                if (available == 0)
                {
                    _isPreBuffering = true;
                    Array.Clear(buffer, offset, count);
                    return count;
                }

                // --- PI Controller for Drift Compensation ---
                // Only active in Low Latency Mode
                if (_lowLatencyMode)
                {
                    int currentError = available - targetSamples;
                    
                    // Aggressive tuning for Low Latency
                    double Kp = 0.000001; // 1e-6
                    double Ki = 0.00000001; // 1e-8

                    _integralError += currentError;
                    
                    // Anti-windup for integral term
                    double maxIntegral = 0.05 / Ki; // Limit integral contribution to 5%
                    _integralError = Math.Clamp(_integralError, -maxIntegral, maxIntegral);

                    double adjustment = (currentError * Kp) + (_integralError * Ki);
                    
                    // Clamp total adjustment
                    adjustment = Math.Clamp(adjustment, -0.005, 0.005);
                    
                    ratio *= (1.0 + adjustment);
                }
                else
                {
                    // Normal Mode: Disable PI Controller.
                    // Use fixed ratio (1.0 if rates match, or fixed resampling ratio).
                    // This acts as "Simple Blocking Read" logic but through the resampler pipeline
                    // to handle potential rate mismatches safely.
                    // No adjustment.
                }

                // --- Resampling Loop (Cubic Interpolation) ---
                
                // Calculate needed input
                // We need enough samples to cover the furthest point we might access (index + 2)
                int neededInput = (int)Math.Ceiling(outSamples * ratio + _resampleFraction) + 4;
                
                int availableInput = _circularBuffer.Available;
                int toReadInput = Math.Min(neededInput, availableInput);
                
                // Read into buffer at offset 0
                int readCount = _circularBuffer.Read(_resampleBuffer.AsSpan(0, toReadInput));
                
                if (readCount == 0)
                {
                    Array.Clear(buffer, offset, count);
                    return count;
                }

                int outIdx = 0;
                int byteIdx = offset;

                while (outIdx < outSamples)
                {
                    double pos = outIdx * ratio + _resampleFraction;
                    int index = (int)pos;
                    float frac = (float)(pos - index);

                    // We need 4 samples: y0, y1, y2, y3
                    // y1 is at 'index'
                    // y2 is at 'index + 1'
                    // y0 is at 'index - 1'
                    // y3 is at 'index + 2'
                    
                    float y0, y1, y2, y3;

                    // Helper to get sample from history or buffer
                    float GetSample(int idx)
                    {
                        if (idx < 0)
                        {
                            // Fetch from history
                            // _inputHistory has 4 elements.
                            // idx = -1 -> last sample (_inputHistory[3])
                            // idx = -2 -> second to last (_inputHistory[2])
                            int histIdx = 4 + idx;
                            if (histIdx >= 0 && histIdx < 4) return _inputHistory[histIdx];
                            return 0f; 
                        }
                        else if (idx >= readCount)
                        {
                            // End of buffer clamp
                            if (readCount > 0) return _resampleBuffer[readCount - 1];
                            return 0f;
                        }
                        else
                        {
                            return _resampleBuffer[idx];
                        }
                    }

                    y0 = GetSample(index - 1);
                    y1 = GetSample(index);
                    y2 = GetSample(index + 1);
                    y3 = GetSample(index + 2);

                    // Cubic Interpolation
                    float sample = Dsp.CubicInterpolate(y0, y1, y2, y3, frac);
                    
                    // Note: Input Gain Safety (-3dB) is now applied at Input stage (OnDataAvailable)
                    // So we don't apply it here again.
                    
                    // No heavy DSP - just write
                    _sampleWriter(buffer, byteIdx, sample);
                    
                    byteIdx += bytesPerSample;
                    outIdx++;
                }

                // Fill remainder
                if (byteIdx < offset + count)
                {
                    Array.Clear(buffer, byteIdx, offset + count - byteIdx);
                }

                // Update History for next block
                if (readCount >= 4)
                {
                    Array.Copy(_resampleBuffer, readCount - 4, _inputHistory, 0, 4);
                }
                else
                {
                    // Shift history and append new data
                    for (int i = 0; i < readCount; i++)
                    {
                        Array.Copy(_inputHistory, 1, _inputHistory, 0, 3);
                        _inputHistory[3] = _resampleBuffer[i];
                    }
                }
                
                // Update fraction relative to the new start of buffer
                _resampleFraction = (outSamples * ratio + _resampleFraction) - readCount;
            }
            catch (Exception ex)
            {
                // Output silence on error
                Log($"Error in Read: {ex.Message}");
                Array.Clear(buffer, offset, count);
                _isRunning = false;
                ErrorOccurred?.Invoke(this, ex);
            }

            return count;
        }

        private void InitializeSampleWriter()
        {
            bool isFloat = WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat;
            bool isPcm = WaveFormat.Encoding == WaveFormatEncoding.Pcm;
            
            if (WaveFormat.Encoding == WaveFormatEncoding.Extensible && WaveFormat is WaveFormatExtensible ext)
            {
                Guid subTypeFloat = new Guid("00000003-0000-0010-8000-00aa00389b71");
                Guid subTypePcm = new Guid("00000001-0000-0010-8000-00aa00389b71");
                
                if (ext.SubFormat == subTypeFloat) isFloat = true;
                else if (ext.SubFormat == subTypePcm) isPcm = true;
            }

            if (isFloat)
            {
                _sampleWriter = WriteSampleFloat;
            }
            else if (isPcm)
            {
                if (WaveFormat.BitsPerSample == 16) _sampleWriter = WriteSamplePcm16;
                else if (WaveFormat.BitsPerSample == 24) _sampleWriter = WriteSamplePcm24;
                else if (WaveFormat.BitsPerSample == 32) _sampleWriter = WriteSamplePcm32;
            }

            if (_sampleWriter == null)
            {
                Log("Unsupported Output Format for Writer. Defaulting to Silence.");
                _sampleWriter = (b, o, s) => { };
            }
        }

        private static void WriteSampleFloat(byte[] buffer, int offset, float sample)
        {
            // Fast path for float
            // BitConverter.TryWriteBytes is safe but we can use unsafe for speed if needed.
            // For now, standard approach is fine.
            int val = BitConverter.SingleToInt32Bits(sample);
            buffer[offset] = (byte)val;
            buffer[offset + 1] = (byte)(val >> 8);
            buffer[offset + 2] = (byte)(val >> 16);
            buffer[offset + 3] = (byte)(val >> 24);
        }

        private static void WriteSamplePcm16(byte[] buffer, int offset, float sample)
        {
            short shortSample = (short)(sample * 32767.0f);
            buffer[offset] = (byte)(shortSample);
            buffer[offset + 1] = (byte)(shortSample >> 8);
        }

        private static void WriteSamplePcm24(byte[] buffer, int offset, float sample)
        {
            int intSample = (int)(sample * 8388607.0f);
            buffer[offset] = (byte)(intSample);
            buffer[offset + 1] = (byte)(intSample >> 8);
            buffer[offset + 2] = (byte)(intSample >> 16);
        }

        private static void WriteSamplePcm32(byte[] buffer, int offset, float sample)
        {
            int intSample = (int)(sample * 2147483647.0f);
            buffer[offset] = (byte)(intSample);
            buffer[offset + 1] = (byte)(intSample >> 8);
            buffer[offset + 2] = (byte)(intSample >> 16);
            buffer[offset + 3] = (byte)(intSample >> 24);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                ErrorOccurred?.Invoke(this, e.Exception);
                DeviceDisconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Cleanup()
        {
            try
            {
                if (_mmcssSetCapture && _mmcssHandleCapture != IntPtr.Zero)
                {
                    Mmcss.AvRevertMmThreadCharacteristics(_mmcssHandleCapture);
                    _mmcssSetCapture = false;
                }
                if (_mmcssSetRender && _mmcssHandleRender != IntPtr.Zero)
                {
                    Mmcss.AvRevertMmThreadCharacteristics(_mmcssHandleRender);
                    _mmcssSetRender = false;
                }

                if (_capture != null)
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    _capture.StopRecording();
                    _capture.Dispose();
                    _capture = null;
                }

                if (_renderer != null)
                {
                    _renderer.Stop();
                    _renderer.Dispose();
                    _renderer = null;
                }
                
                _circularBuffer = null;
            }
            catch
            {
                // Suppress cleanup errors
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
