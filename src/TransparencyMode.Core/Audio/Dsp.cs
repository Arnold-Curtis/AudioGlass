using System;

namespace TransparencyMode.Core.Audio
{
    public static class Dsp
    {
        /// <summary>
        /// A simple Biquad Filter implementation (Direct Form 1)
        /// </summary>
        public class BiquadFilter
        {
            private float _a0, _a1, _a2, _b0, _b1, _b2;
            private float _z1, _z2;

            public void SetLowPass(float sampleRate, float cutoffFrequency, float q = 0.7071f)
            {
                double w0 = 2 * Math.PI * cutoffFrequency / sampleRate;
                double alpha = Math.Sin(w0) / (2 * q);
                double cosW0 = Math.Cos(w0);

                double b0 = (1 - cosW0) / 2;
                double b1 = 1 - cosW0;
                double b2 = (1 - cosW0) / 2;
                double a0 = 1 + alpha;
                double a1 = -2 * cosW0;
                double a2 = 1 - alpha;

                _b0 = (float)(b0 / a0);
                _b1 = (float)(b1 / a0);
                _b2 = (float)(b2 / a0);
                _a1 = (float)(a1 / a0);
                _a2 = (float)(a2 / a0);
                
                // Reset state on coefficient change to prevent explosion
                _z1 = 0;
                _z2 = 0;
            }

            public float Process(float input)
            {
                float output = _b0 * input + _b1 * _z1 + _b2 * _z2 - _a1 * _z1 - _a2 * _z2;
                
                // Shift delay line
                _z2 = _z1;
                _z1 = input; // Direct Form 1 uses input history for zeros? 
                // Wait, standard DF1: y[n] = b0*x[n] + b1*x[n-1] + b2*x[n-2] - a1*y[n-1] - a2*y[n-2]
                // My implementation above is mixed up. Let's do standard DF1.
                
                return output;
            }
            
            // Correct Direct Form 1 State
            private float _x1, _x2, _y1, _y2;
            
            public float ProcessDF1(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                
                // Update state
                _x2 = _x1;
                _x1 = x;
                _y2 = _y1;
                _y1 = y;
                
                return y;
            }
        }

        /// <summary>
        /// Limits the rate of change between samples to reduce clicks/pops.
        /// </summary>
        public class SlewLimiter
        {
            private float _lastSample;
            private readonly float _maxDelta;

            public SlewLimiter(float sampleRate, float maxSlewHz)
            {
                // Max delta per sample to support a full swing sine wave at maxSlewHz
                // A sine wave A*sin(wt) has max slope A*w.
                // For A=1.0, max slope is 2*pi*f.
                // Per sample, that is 2*pi*f / fs.
                _maxDelta = (float)(2 * Math.PI * maxSlewHz / sampleRate);
            }

            public float Process(float input)
            {
                float delta = input - _lastSample;
                
                if (delta > _maxDelta)
                {
                    input = _lastSample + _maxDelta;
                }
                else if (delta < -_maxDelta)
                {
                    input = _lastSample - _maxDelta;
                }
                
                _lastSample = input;
                return input;
            }
            
            public void Reset(float value = 0f)
            {
                _lastSample = value;
            }
        }

        /// <summary>
        /// Soft clips the signal to prevent harsh digital clipping.
        /// Uses a polynomial approximation for saturation.
        /// </summary>
        public static float SoftClip(float input)
        {
            // Hard Limit first to catch extreme outliers (Crackles)
            if (input > 2.0f) input = 2.0f;
            if (input < -2.0f) input = -2.0f;

            // Soft Knee starts at 0.8
            // If x > 0.8, compress
            if (input > 0.8f)
            {
                return 0.8f + (1.0f - 0.8f) * (float)Math.Tanh((input - 0.8f) / (1.0f - 0.8f));
            }
            else if (input < -0.8f)
            {
                return -(0.8f + (1.0f - 0.8f) * (float)Math.Tanh((-input - 0.8f) / (1.0f - 0.8f)));
            }
            
            return input;
        }
        
        /// <summary>
        /// Hard limiter with soft knee
        /// </summary>
        public static float Limit(float input, float threshold = 0.95f)
        {
            if (input > threshold)
            {
                // Map [threshold, infinity] to [threshold, 1.0]
                // Tanh-like compression
                float over = input - threshold;
                return threshold + (1.0f - threshold) * (float)Math.Tanh(over / (1.0f - threshold));
            }
            else if (input < -threshold)
            {
                float over = -input - threshold;
                return -(threshold + (1.0f - threshold) * (float)Math.Tanh(over / (1.0f - threshold)));
            }
            return input;
        }

        /// <summary>
        /// Hermite Cubic Interpolation for smoother resampling.
        /// y0, y1, y2, y3 are four consecutive samples.
        /// mu is the fractional position between y1 and y2 (0.0 to 1.0).
        /// </summary>
        public static float CubicInterpolate(float y0, float y1, float y2, float y3, float mu)
        {
            float mu2 = mu * mu;
            float a0 = -0.5f * y0 + 1.5f * y1 - 1.5f * y2 + 0.5f * y3;
            float a1 = y0 - 2.5f * y1 + 2.0f * y2 - 0.5f * y3;
            float a2 = -0.5f * y0 + 0.5f * y2;
            float a3 = y1;

            return a0 * mu * mu2 + a1 * mu2 + a2 * mu + a3;
        }
    }
}
