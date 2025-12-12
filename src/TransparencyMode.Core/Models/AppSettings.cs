namespace TransparencyMode.Core.Models
{
    /// <summary>
    /// Application settings for persistence
    /// </summary>
    public class AppSettings
    {
        public string? LastInputDeviceId { get; set; }
        public string? LastOutputDeviceId { get; set; }
        public float Volume { get; set; } = 1.0f;
        public bool IsEnabled { get; set; } = true;
        public int BufferMilliseconds { get; set; } = 10;
        public bool LowLatencyMode { get; set; } = false;
    }
}
