using System;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Moonshine.UI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Moonshine",
        "settings.json"
    );

    private string _defaultCodec = "HEVC (H.265)";
    public string DefaultCodec
    {
        get => _defaultCodec;
        set
        {
            if (SetProperty(ref _defaultCodec, value))
            {
                SaveSettings();
            }
        }
    }

    private int _defaultBitrateKbps = 20000;
    public int DefaultBitrateKbps
    {
        get => _defaultBitrateKbps;
        set
        {
            if (SetProperty(ref _defaultBitrateKbps, value))
            {
                SaveSettings();
            }
        }
    }

    private int _defaultFps = 60;
    public int DefaultFps
    {
        get => _defaultFps;
        set
        {
            if (SetProperty(ref _defaultFps, value))
            {
                OnPropertyChanged(nameof(SelectedFpsString));
                SaveSettings();
            }
        }
    }

    public string SelectedFpsString
    {
        get => _defaultFps.ToString();
        set
        {
            if (int.TryParse(value, out int v))
            {
                DefaultFps = v;
            }
        }
    }

    private string _audioSink = "WASAPI Exclusive (Low Latency)";
    public string AudioSink
    {
        get => _audioSink;
        set
        {
            if (SetProperty(ref _audioSink, value))
            {
                SaveSettings();
            }
        }
    }

    private bool _enableHdr10;
    public bool EnableHdr10
    {
        get => _enableHdr10;
        set
        {
            if (SetProperty(ref _enableHdr10, value))
            {
                SaveSettings();
            }
        }
    }

    private int _jitterBufferTargetMs = 16;
    public int JitterBufferTargetMs
    {
        get => _jitterBufferTargetMs;
        set
        {
            if (SetProperty(ref _jitterBufferTargetMs, value))
            {
                SaveSettings();
            }
        }
    }

    public SettingsViewModel()
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty(nameof(DefaultCodec), out var c)) _defaultCodec = c.GetString() ?? _defaultCodec;
                if (root.TryGetProperty(nameof(DefaultBitrateKbps), out var b)) _defaultBitrateKbps = b.GetInt32();
                if (root.TryGetProperty(nameof(DefaultFps), out var f)) _defaultFps = f.GetInt32();
                if (root.TryGetProperty(nameof(AudioSink), out var a)) _audioSink = a.GetString() ?? _audioSink;
                if (root.TryGetProperty(nameof(EnableHdr10), out var h)) _enableHdr10 = h.GetBoolean();
                if (root.TryGetProperty(nameof(JitterBufferTargetMs), out var j)) _jitterBufferTargetMs = j.GetInt32();
            }
        }
        // ALLOWED_EXCEPTION: Fallback to defaults when settings file is absent or invalid JSON.
        catch (Exception)
        {
        }
    }

    private void SaveSettings()
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var model = new
            {
                DefaultCodec = _defaultCodec,
                DefaultBitrateKbps = _defaultBitrateKbps,
                DefaultFps = _defaultFps,
                AudioSink = _audioSink,
                EnableHdr10 = _enableHdr10,
                JitterBufferTargetMs = _jitterBufferTargetMs
            };

            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(model, s_jsonOptions));
        }
        // ALLOWED_EXCEPTION: Ignore transient file IO exceptions during background settings autosave.
        catch (Exception)
        {
        }
    }
}
