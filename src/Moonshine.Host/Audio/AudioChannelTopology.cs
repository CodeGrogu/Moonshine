namespace Moonshine.Host.Audio;

/// <summary>
/// Audio channel topology configurations for low-latency streaming.
/// </summary>
public enum AudioChannelTopology
{
    None = 0,
    Stereo = 2,
    Surround51 = 6,
    Surround71 = 8
}
