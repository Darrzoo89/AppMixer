using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using AppMixer.Audio;

namespace AppMixer.ViewModels;

public sealed class DeviceItem(string id, string name, bool isDefault)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public bool IsDefault { get; } = isDefault;
}

public sealed class MainViewModel : ObservableObject
{
    private const string SystemSoundsKey = "#system";
    private static readonly TimeSpan UserEditGrace = TimeSpan.FromMilliseconds(700);

    private readonly AudioEngine _engine = new();
    private readonly ProcessInfoCache _processes = new();
    private readonly DispatcherTimer _slowTimer;
    private readonly DispatcherTimer _meterTimer;

    private DeviceHandle? _device;
    private string? _defaultDeviceId;
    private string _deviceSignature = "";
    private bool _followDefault = true;
    private bool _rebuildingDevices;
    private DateTime _lastMasterEdit = DateTime.MinValue;

    public ObservableCollection<DeviceItem> Devices { get; } = new();
    public ObservableCollection<AppViewModel> Apps { get; } = new();
    public ICommand ToggleMasterMuteCommand { get; }

    public MainViewModel(bool playingOnly)
    {
        _playingOnly = playingOnly;
        ToggleMasterMuteCommand = new RelayCommand(() => MasterMuted = !MasterMuted);

        _slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _slowTimer.Tick += (_, _) => Refresh();
        _meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => UpdateMeters();

        Refresh();
        _slowTimer.Start(); // keeps running while hidden so the tray tooltip stays current
    }

    /// <summary>Stop the 30fps meter updates while the window is minimized.</summary>
    public bool MetersEnabled
    {
        set { if (value) _meterTimer.Start(); else _meterTimer.Stop(); }
    }

    // ---------- Output device ----------

    private DeviceItem? _selectedDevice;
    public DeviceItem? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!Set(ref _selectedDevice, value) || _rebuildingDevices || value == null) return;
            _followDefault = value.Id == _defaultDeviceId;
            OpenSelectedDevice();
        }
    }

    private void RefreshDevices()
    {
        var devices = _engine.GetOutputDevices();
        var defaultId = _engine.GetDefaultDeviceId();
        var signature = defaultId + "|" + string.Join("|", devices.Select(d => d.Id + "=" + d.Name));
        if (signature == _deviceSignature) return;
        _deviceSignature = signature;
        _defaultDeviceId = defaultId;

        string? wantId = _followDefault ? defaultId : _device?.Id;
        if (wantId == null || devices.All(d => d.Id != wantId))
        {
            wantId = defaultId;
            _followDefault = true;
        }

        _rebuildingDevices = true;
        Devices.Clear();
        foreach (var d in devices.OrderByDescending(d => d.Id == defaultId).ThenBy(d => d.Name))
            Devices.Add(new DeviceItem(d.Id, d.Name, d.Id == defaultId));
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == wantId) ?? Devices.FirstOrDefault();
        _rebuildingDevices = false;

        OpenSelectedDevice();
    }

    private void OpenSelectedDevice()
    {
        var id = _selectedDevice?.Id;
        if (id == _device?.Id) return;

        _device = id == null ? null : _engine.OpenDevice(id);
        Apps.Clear();
        _lastMasterEdit = DateTime.MinValue;
        RefreshSessions();
        SyncMaster();
    }

    // ---------- Master volume ----------

    private double _masterVolume;
    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (!Set(ref _masterVolume, value)) return;
            _lastMasterEdit = DateTime.UtcNow;
            TryDevice(d => d.Volume = (float)(value / 100.0));
            if (_masterMuted && value > 0) MasterMuted = false;
            OnPropertyChanged(nameof(MasterGlyph));
        }
    }

    private bool _masterMuted;
    public bool MasterMuted
    {
        get => _masterMuted;
        set
        {
            if (!Set(ref _masterMuted, value)) return;
            _lastMasterEdit = DateTime.UtcNow;
            TryDevice(d => d.Muted = value);
            OnPropertyChanged(nameof(MasterGlyph));
        }
    }

    public string MasterGlyph => VolumeGlyphs.For(_masterVolume, _masterMuted);

    private double _masterPeak;
    public double MasterPeak { get => _masterPeak; private set => Set(ref _masterPeak, value); }

    private void SyncMaster()
    {
        if (_device == null || DateTime.UtcNow - _lastMasterEdit < UserEditGrace) return;
        try
        {
            double vol = Math.Round(_device.Volume * 100.0);
            bool muted = _device.Muted;
            if (Math.Abs(vol - _masterVolume) > 0.01) { _masterVolume = vol; OnPropertyChanged(nameof(MasterVolume)); }
            if (muted != _masterMuted) { _masterMuted = muted; OnPropertyChanged(nameof(MasterMuted)); }
            OnPropertyChanged(nameof(MasterGlyph));
        }
        catch (COMException) { }
    }

    private void TryDevice(Action<DeviceHandle> action)
    {
        if (_device == null) return;
        try { action(_device); } catch (COMException) { }
    }

    // ---------- Apps ----------

    private bool _playingOnly;
    /// <summary>Only show apps that made sound in the last few seconds.</summary>
    public bool PlayingOnly
    {
        get => _playingOnly;
        set { if (Set(ref _playingOnly, value)) UpdateVisibility(); }
    }

    private bool _hasNoVisibleApps;
    public bool HasNoVisibleApps { get => _hasNoVisibleApps; private set => Set(ref _hasNoVisibleApps, value); }

    public string EmptyMessage => _device == null
        ? "No output device found."
        : PlayingOnly ? "Nothing is playing sound right now." : "No apps are using this device right now.";

    private void Refresh()
    {
        try { RefreshDevices(); } catch (COMException) { }
        RefreshSessions();
        SyncMaster();
    }

    private void RefreshSessions()
    {
        List<AudioSession> sessions;
        try { sessions = _device?.GetSessions() ?? new(); }
        catch (COMException) { sessions = new(); }

        // Group the sessions into one row per app.
        var groups = new Dictionary<string, (string Name, ProcessInfo? Info, List<AudioSession> Sessions)>();
        var seenPids = new HashSet<uint>();
        foreach (var s in sessions)
        {
            string key;
            ProcessInfo? info = null;
            if (s.IsSystemSounds)
            {
                key = SystemSoundsKey;
            }
            else
            {
                info = _processes.Get(s.ProcessId);
                if (info == null) continue; // the app has closed
                seenPids.Add(s.ProcessId);
                key = info.GroupKey;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = (NameFor(s, info), info, new List<AudioSession>());
                groups[key] = group;
            }
            group.Sessions.Add(s);
        }
        _processes.Prune(seenPids);

        for (int i = Apps.Count - 1; i >= 0; i--)
            if (!groups.ContainsKey(Apps[i].Key)) Apps.RemoveAt(i);

        foreach (var (key, group) in groups)
        {
            var app = Apps.FirstOrDefault(a => a.Key == key);
            if (app == null)
            {
                app = new AppViewModel(key, group.Name, group.Info?.Icon, key == SystemSoundsKey);
                InsertSorted(app);
            }
            app.SetSessions(group.Sessions);
            app.SyncFromSystem();
        }

        UpdateVisibility();
    }

    private static string NameFor(AudioSession session, ProcessInfo? info)
    {
        if (session.IsSystemSounds) return "System Sounds";
        var display = IndirectString.Resolve(session.DisplayName).Trim();
        if (!string.IsNullOrEmpty(display)) return display;
        return info?.Description ?? info?.ProcessName ?? "Unknown app";
    }

    private void InsertSorted(AppViewModel app)
    {
        int index = 0;
        while (index < Apps.Count && Compare(Apps[index], app) <= 0) index++;
        Apps.Insert(index, app);
    }

    private static int Compare(AppViewModel a, AppViewModel b)
    {
        if (a.IsSystemSounds != b.IsSystemSounds) return a.IsSystemSounds ? -1 : 1;
        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateVisibility()
    {
        foreach (var app in Apps)
            app.IsVisible = !PlayingOnly || app.PlayedRecently;
        HasNoVisibleApps = !Apps.Any(a => a.IsVisible);
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private void UpdateMeters()
    {
        if (_device != null)
        {
            try
            {
                float raw = _device.Peak;
                double p = Math.Max(raw, _masterPeak * 0.82);
                MasterPeak = p < 0.002 ? 0 : p;
            }
            catch (COMException) { }
        }

        foreach (var app in Apps)
        {
            bool wasVisible = app.IsVisible;
            app.UpdateMeter();
            // Pop newly-playing apps in right away instead of waiting for the next refresh.
            if (PlayingOnly && !wasVisible && app.PlayedRecently)
            {
                app.IsVisible = true;
                HasNoVisibleApps = false;
            }
        }
    }
}
