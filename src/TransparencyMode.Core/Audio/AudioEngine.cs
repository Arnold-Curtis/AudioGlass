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
        private float[]? _resampleBuffer;   // For Resampling
        
        private volatile bool _isRunning;
        private volatile float _volume = 1.0f;
        private int _bufferMilliseconds = 10;
        private bool _lowLatencyMode = true;
        
        // Resampling State
        private float _lastSample = 0f;
        private double _resampleFraction = 0.0;

        // MMCSS State
        private bool _mmcssSetCapture = false;
        private bool _mmcssSetRender = false;
        private IntPtr _mmcssHandleCapture = IntPtr.Zero;
        private IntPtr _mmcssHandleRender = IntPtr.Zero;

        public event EventHandler<Exception>? ErrorOccurred;
        public event EventHandler? DeviceDisconnected;

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

        public AudioEngine()
        {
            // Default format until initialized
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        }

        /// <summary>
        /// Starts the audio passthrough engine
        /// </summary>
        public void Start(MMDevice inputDevice, MMDevice outputDevice)
        {
            if (_isRunning) Stop();

            try
            {
                _inputDevice = inputDevice;
                _outputDevice = outputDevice;

                // Request Hardware Minimums
                if (_lowLatencyMode)
                {
                    try
                    {
                        // AudioClient.DevicePeriod returns value in 100-nanosecond units
                        // 1 ms = 10,000 units
                        long minPeriod = _outputDevice.AudioClient.MinimumDevicePeriod;
                        int minPeriodMs = (int)(minPeriod / 10000);
                        
                        // Use minimum period but ensure it's at least 3ms to be safe
                        _bufferMilliseconds = Math.Max(minPeriodMs, 3);
                    }
                    catch
                    {
                        // Fallback to default if query fails
                        _bufferMilliseconds = 10;
                    }
                }

                // 1. Initialize Capture (Event Driven)
                // UseEventSync = true puts WASAPI in Event Driven mode
                // Note: AudioClientStreamOptions.Raw is not directly supported by standard WasapiCapture constructor in this version.
                // We rely on low buffer size and exclusive mode (if applicable) or just low latency shared mode.
                _capture = new WasapiCapture(_inputDevice, true, _bufferMilliseconds);
                
                // 2. Setup Circular Buffer
                // Capacity: 65536 samples (approx 1.3s @ 48kHz) - Power of 2
                _circularBuffer = new LockFreeCircularBuffer(65536);
                
                // 3. Determine Output Format
                // We prefer the device's native MixFormat to avoid Windows mixer resampling
                WaveFormat = _outputDevice.AudioClient.MixFormat;
                
                // 4. Pre-allocate processing buffers
                // Allocate enough for ~100ms to be safe against jitter
                int maxSamples = 48000 * 2 / 10; // 100ms
                _conversionBuffer = new float[maxSamples];
                _resampleBuffer = new float[maxSamples];
                
                // Reset State
                _lastSample = 0f;
                _resampleFraction = 0.0;
                _mmcssSetCapture = false;
                _mmcssSetRender = false;

                // 5. Initialize Renderer (Event Driven)
                // We pass 'this' as the IWaveProvider
                _renderer = new WasapiOut(_outputDevice, AudioClientShareMode.Shared, true, _bufferMilliseconds);
                _renderer.Init(this);

                // 6. Hook Events
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _isRunning = true;
                
                // Start
                _capture.StartRecording();
                _renderer.Play();
            }
            catch (Exception ex)
            {
                _isRunning = false;
                ErrorOccurred?.Invoke(this, ex);
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
        }

        /// <summary>
        /// Capture Callback: Runs on NAudio's Capture Thread
        /// </summary>
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRunning || _circularBuffer == null || _conversionBuffer == null) return;

            try
            {
                // Set Thread Priority to Pro Audio once
                if (!_mmcssSetCapture)
                {
                    uint taskIndex = 0;
                    _mmcssHandleCapture = Mmcss.AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
                    _mmcssSetCapture = true;
                }

                // 1. Convert Bytes to Float
                int bytesRecorded = e.BytesRecorded;
                int samplesRead = 0;
                var format = _capture!.WaveFormat;

                if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    samplesRead = bytesRecorded / 4;
                    // Bulk copy/cast if possible, but we need volume application
                    // Using loop for volume + clamp
                    for (int i = 0; i < samplesRead; i++)
                    {
                        float sample = BitConverter.ToSingle(e.Buffer, i * 4);
                        _conversionBuffer[i] = Math.Clamp(sample * _volume, -1.0f, 1.0f);
                    }
                }
                else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
                {
                    samplesRead = bytesRecorded / 2;
                    for (int i = 0; i < samplesRead; i++)
                    {
                        short sample = BitConverter.ToInt16(e.Buffer, i * 2);
                        float normalized = sample / 32768.0f;
                        _conversionBuffer[i] = Math.Clamp(normalized * _volume, -1.0f, 1.0f);
                    }
                }
                // Add other formats if needed (24-bit, etc.)

                // 2. Write to Circular Buffer
                if (samplesRead > 0)
                {
                    _circularBuffer.Write(new ReadOnlySpan<float>(_conversionBuffer, 0, samplesRead));
                }
            }
            catch (Exception)
            {
                // Don't throw on audio thread, just log/notify
            }
        }

        /// <summary>
        /// Render Callback: Called by WasapiOut (IWaveProvider.Read)
        /// Runs on NAudio's Render Thread
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (!_isRunning || _circularBuffer == null || _resampleBuffer == null)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            try
            {
                // Set Thread Priority to Pro Audio once
                if (!_mmcssSetRender)
                {
                    uint taskIndex = 0;
                    _mmcssHandleRender = Mmcss.AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
                    _mmcssSetRender = true;
                }

                // Latency Clamping (Aggressive Catch-Up)
                if (_lowLatencyMode)
                {
                    // Target: 5ms - 7ms
                    int targetLatencySamples = (int)(WaveFormat.SampleRate * 0.005); // 5ms
                    int available = _circularBuffer.Available;
                    
                    if (available > targetLatencySamples)
                    {
                        int dropCount = available - targetLatencySamples;
                        _circularBuffer.AdvanceRead(dropCount);
                    }
                }

                int bytesPerSample = WaveFormat.BitsPerSample / 8;
                int outSamples = count / bytesPerSample;
                
                // Calculate Resampling Ratio
                double inRate = _capture?.WaveFormat.SampleRate ?? 44100;
                double outRate = WaveFormat.SampleRate;
                double ratio = inRate / outRate;

                // Fast path: No resampling needed
                if (Math.Abs(ratio - 1.0) < 0.001)
                {
                    int available = _circularBuffer.Available;
                    int toRead = Math.Min(outSamples, available);
                    
                    int read = _circularBuffer.Read(_resampleBuffer.AsSpan(0, toRead));
                    
                    // Convert to Output Bytes
                    int outOffset = offset;
                    for (int i = 0; i < read; i++)
                    {
                        WriteSample(buffer, outOffset, _resampleBuffer[i]);
                        outOffset += bytesPerSample;
                    }
                    
                    // Fill remainder with silence (Underrun)
                    if (read < outSamples)
                    {
                        Array.Clear(buffer, outOffset, count - (read * bytesPerSample));
                    }
                    
                    // Update last sample for continuity if we switch to resampling later
                    if (read > 0) _lastSample = _resampleBuffer[read - 1];
                    
                    return count;
                }

                // Resampling Path (Linear Interpolation)
                // Calculate how many input samples we need to generate 'outSamples'
                // We need to cover the range from _resampleFraction to (outSamples * ratio + _resampleFraction)
                int neededInput = (int)Math.Ceiling(outSamples * ratio + _resampleFraction);
                
                // Read from buffer
                int availableInput = _circularBuffer.Available;
                int toReadInput = Math.Min(neededInput, availableInput);
                
                int readCount = _circularBuffer.Read(_resampleBuffer.AsSpan(0, toReadInput));
                
                // If we have no data, output silence
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
                    
                    // Check if we have enough data for this sample
                    // We need index and index+1 (unless index is last, then we clamp)
                    if (index >= readCount)
                    {
                        // Underrun during resampling loop
                        break;
                    }

                    float frac = (float)(pos - index);
                    
                    // y1: If index is -1 (from negative fraction), use _lastSample
                    // This happens if _resampleFraction was negative from previous over-read
                    float y1;
                    if (index < 0) y1 = _lastSample;
                    else y1 = _resampleBuffer[index];

                    // y2: Next sample
                    float y2;
                    if (index + 1 < readCount) y2 = _resampleBuffer[index + 1];
                    else if (index + 1 == readCount) y2 = y1; // Clamp at end
                    else y2 = y1; // Should not happen if check above is correct

                    // Linear Interpolation
                    float sample = y1 + (y2 - y1) * frac;
                    
                    WriteSample(buffer, byteIdx, sample);
                    
                    byteIdx += bytesPerSample;
                    outIdx++;
                }

                // Fill remainder with silence
                if (byteIdx < offset + count)
                {
                    Array.Clear(buffer, byteIdx, offset + count - byteIdx);
                }

                // Update State
                if (readCount > 0)
                    _lastSample = _resampleBuffer[readCount - 1];
                
                // Update fraction relative to the new start of buffer (which is 'readCount' samples forward)
                _resampleFraction = (outSamples * ratio + _resampleFraction) - readCount;
            }
            catch (Exception)
            {
                // Output silence on error
                Array.Clear(buffer, offset, count);
            }

            return count;
        }

        private void WriteSample(byte[] buffer, int offset, float sample)
        {
            // Clamp
            sample = Math.Clamp(sample, -1.0f, 1.0f);

            if (WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                BitConverter.TryWriteBytes(new Span<byte>(buffer, offset, 4), sample);
            }
            else if (WaveFormat.Encoding == WaveFormatEncoding.Pcm && WaveFormat.BitsPerSample == 16)
            {
                short shortSample = (short)(sample * 32767.0f);
                buffer[offset] = (byte)(shortSample & 0xFF);
                buffer[offset + 1] = (byte)((shortSample >> 8) & 0xFF);
            }
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
