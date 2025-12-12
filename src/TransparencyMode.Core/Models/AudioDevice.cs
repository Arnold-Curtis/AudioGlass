namespace TransparencyMode.Core.Models
{
    /// <summary>
    /// Represents an audio device with its properties
    /// </summary>
    public class AudioDevice
    {
        public string Id { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }

        public override string ToString() => FriendlyName;

        public override bool Equals(object? obj)
        {
            if (obj is AudioDevice other)
            {
                return Id == other.Id;
            }
            return false;
        }

        public override int GetHashCode() => Id.GetHashCode();
    }
}
