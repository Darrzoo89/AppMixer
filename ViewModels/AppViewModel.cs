using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media;
using AppMixer.Audio;

namespace AppMixer.ViewModels;

/// <summary>One row in the mixer: an app and all of its audio streams.</summary>
public sealed class AppViewModel : ObservableObject
{
    private static readonly TimeSpan UserEditGrace = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan AudibleHold = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan RecentlyPlayedWindow = TimeSpan.FromSeconds(10);

    private List<AudioSession> _sessions = new();
    private DateTime _lastUserEdit = DateTime.MinValue;
    private DateTime _lastAudible = DateTime.MinValue;

    public string Key { get; }
    public string Name { get; }
    public ImageSource? Icon { get; }
    public bool IsSystemSounds { get; }
    public ICommand ToggleMuteCommand { get; }

    public AppViewModel(string key, string name, ImageSource? icon, bool isSystemSounds)
    {
        Key = key;
        Name = name;
        Icon = icon;
        IsSystemSounds = isSystemSounds;
        ToggleMuteCommand = new RelayCommand(() => Muted = !Muted);
    }

    /// <summary>Fallback icon when the app has none (Segoe MDL2 glyph).</summary>
    public string FallbackGlyph => IsSystemSounds ? "" : "";

    private double _volume;
    /// <summary>0–100. Setting it changes the app's real volume.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (!Set(ref _volume, value)) return;
            _lastUserEdit = DateTime.UtcNow;
            ForEachSession(s => s.Volume = (float)(value / 100.0));
            if (_muted && value > 0) Muted = false; // dragging a muted slider unmutes it
            OnPropertyChanged(nameof(VolumeGlyph));
        }
    }

    private bool _muted;
    public bool Muted
    {
        get => _muted;
        set
        {
            if (!Set(ref _muted, value)) return;
            _lastUserEdit = DateTime.UtcNow;
            ForEachSession(s => s.Muted = value);
            OnPropertyChanged(nameof(VolumeGlyph));
        }
    }

    public string VolumeGlyph => VolumeGlyphs.For(_volume, _muted);

    private double _peak;
    /// <summary>0–1 live level for the meter bar.</summary>
    public double Peak { get => _peak; private set => Set(ref _peak, value); }

    private bool _isAudible;
    /// <summary>True while the app is making sound right now.</summary>
    public bool IsAudible { get => _isAudible; private set => Set(ref _isAudible, value); }

    public bool PlayedRecently => DateTime.UtcNow - _lastAudible < RecentlyPlayedWindow;

    private bool _isVisible = true;
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

    public void SetSessions(List<AudioSession> sessions) => _sessions = sessions;

    /// <summary>Pull volume/mute from Windows, in case something else changed them.</summary>
    public void SyncFromSystem()
    {
        if (DateTime.UtcNow - _lastUserEdit < UserEditGrace || _sessions.Count == 0) return;
        try
        {
            var s = _sessions[0];
            double vol = Math.Round(s.Volume * 100.0);
            bool muted = s.Muted;
            if (Math.Abs(vol - _volume) > 0.01) { _volume = vol; OnPropertyChanged(nameof(Volume)); }
            if (muted != _muted) { _muted = muted; OnPropertyChanged(nameof(Muted)); }
            OnPropertyChanged(nameof(VolumeGlyph));
        }
        catch (COMException) { }
    }

    public void UpdateMeter()
    {
        float raw = 0;
        foreach (var s in _sessions)
        {
            try { raw = Math.Max(raw, s.Peak); } catch (COMException) { }
        }

        var now = DateTime.UtcNow;
        if (raw > 0.003f) _lastAudible = now;
        // Fast attack, smooth fall-off so the bar doesn't flicker.
        Peak = Math.Max(raw, _peak * 0.82);
        if (Peak < 0.002) Peak = 0;
        IsAudible = now - _lastAudible < AudibleHold;
    }

    private void ForEachSession(Action<AudioSession> action)
    {
        foreach (var s in _sessions)
        {
            try { action(s); } catch (COMException) { /* session ended */ }
        }
    }
}
