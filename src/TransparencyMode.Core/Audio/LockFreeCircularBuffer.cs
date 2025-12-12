using System;
using System.Threading;

namespace TransparencyMode.Core.Audio
{
    /// <summary>
    /// A lock-free circular buffer for float samples.
    /// Designed for Single-Producer, Single-Consumer (SPSC) scenarios.
    /// Uses Interlocked operations to manage indices safely across threads.
    /// </summary>
    public class LockFreeCircularBuffer
    {
        private readonly float[] _buffer;
        private readonly int _capacity;
        private readonly int _mask;
        private long _writeIndex;
        private long _readIndex;

        /// <summary>
        /// Initializes a new instance of the LockFreeCircularBuffer class.
        /// </summary>
        /// <param name="capacity">Buffer capacity. Must be a power of 2.</param>
        public LockFreeCircularBuffer(int capacity = 65536)
        {
            // Ensure capacity is power of 2 for efficient masking
            if ((capacity & (capacity - 1)) != 0)
            {
                throw new ArgumentException("Capacity must be a power of 2", nameof(capacity));
            }

            _capacity = capacity;
            _mask = capacity - 1;
            _buffer = new float[capacity];
            _writeIndex = 0;
            _readIndex = 0;
        }

        /// <summary>
        /// Gets the number of samples currently available to be read.
        /// </summary>
        public int Available => (int)(Interlocked.Read(ref _writeIndex) - Interlocked.Read(ref _readIndex));

        /// <summary>
        /// Writes data to the buffer.
        /// </summary>
        /// <param name="input">The input span of float samples.</param>
        public void Write(ReadOnlySpan<float> input)
        {
            int count = input.Length;
            long currentWrite = _writeIndex; // Only this thread writes to _writeIndex
            int offset = (int)(currentWrite & _mask);

            int firstChunk = Math.Min(count, _capacity - offset);

            // Copy first chunk
            input.Slice(0, firstChunk).CopyTo(_buffer.AsSpan(offset));

            // Copy second chunk if wrapped
            if (firstChunk < count)
            {
                input.Slice(firstChunk).CopyTo(_buffer.AsSpan(0));
            }

            // Publish the new write index
            // Using Interlocked.Add acts as a memory barrier ensuring data is visible before index update
            Interlocked.Add(ref _writeIndex, count);
        }

        /// <summary>
        /// Reads data from the buffer.
        /// </summary>
        /// <param name="output">The output span to fill.</param>
        /// <returns>The number of samples actually read.</returns>
        public int Read(Span<float> output)
        {
            int count = output.Length;
            long currentRead = _readIndex; // Only this thread writes to _readIndex
            long currentWrite = Interlocked.Read(ref _writeIndex); // Read latest write index

            int available = (int)(currentWrite - currentRead);
            int toRead = Math.Min(count, available);

            if (toRead <= 0) return 0;

            int offset = (int)(currentRead & _mask);
            int firstChunk = Math.Min(toRead, _capacity - offset);

            // Copy first chunk
            _buffer.AsSpan(offset, firstChunk).CopyTo(output.Slice(0, firstChunk));

            // Copy second chunk
            if (firstChunk < toRead)
            {
                _buffer.AsSpan(0, toRead - firstChunk).CopyTo(output.Slice(firstChunk));
            }

            // Publish the new read index
            Interlocked.Add(ref _readIndex, toRead);
            return toRead;
        }

        /// <summary>
        /// Advances the read index without copying data.
        /// Used for latency management to drop old samples.
        /// </summary>
        /// <param name="count">Number of samples to skip.</param>
        public void AdvanceRead(int count)
        {
            if (count <= 0) return;

            long currentRead = _readIndex;
            long currentWrite = Interlocked.Read(ref _writeIndex);

            int available = (int)(currentWrite - currentRead);
            int toSkip = Math.Min(count, available);

            if (toSkip > 0)
            {
                Interlocked.Add(ref _readIndex, toSkip);
            }
        }

        /// <summary>
        /// Resets the buffer by moving read index to write index.
        /// </summary>
        public void Clear()
        {
            Interlocked.Exchange(ref _readIndex, Interlocked.Read(ref _writeIndex));
        }
    }
}
