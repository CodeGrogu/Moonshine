using CommunityToolkit.Mvvm.ComponentModel;

namespace Moonshine.UI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private string _defaultCodec = "HEVC (H.265)";
    public string DefaultCodec
    {
        get => _defaultCodec;
        set => SetProperty(ref _defaultCodec, value);
    }

    private int _defaultBitrateKbps = 20000;
    public int DefaultBitrateKbps
    {
        get => _defaultBitrateKbps;
        set => SetProperty(ref _defaultBitrateKbps, value);
    }

    private int _defaultFps = 60;
    public int DefaultFps
    {
        get => _defaultFps;
        set => SetProperty(ref _defaultFps, value);
    }

    private string _audioSink = "WASAPI Exclusive (Low Latency)";
    public string AudioSink
    {
        get => _audioSink;
        set => SetProperty(ref _audioSink, value);
    }

    private bool _enableHdr10;
    public bool EnableHdr10
    {
        get => _enableHdr10;
        set => SetProperty(ref _enableHdr10, value);
    }

    private int _jitterBufferTargetMs = 16;
    public int JitterBufferTargetMs
    {
        get => _jitterBufferTargetMs;
        set => SetProperty(ref _jitterBufferTargetMs, value);
    }
}
