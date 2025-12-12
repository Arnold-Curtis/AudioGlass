using System;
using System.Runtime.InteropServices;

namespace TransparencyMode.Core.Audio
{
    /// <summary>
    /// Helper class for Multimedia Class Scheduler Service (MMCSS)
    /// Used to boost thread priority for low-latency audio
    /// </summary>
    public static class Mmcss
    {
        [DllImport("avrt.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

        [DllImport("avrt.dll", SetLastError = true)]
        public static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);
    }
}
