using System.Runtime.InteropServices;
using System.Text;
using AppMixer.Audio.Interop;

namespace AppMixer.Audio;

public sealed record OutputDevice(string Id, string Name);

/// <summary>Finds output devices and opens them.</summary>
public sealed class AudioEngine
{
    private readonly IMMDeviceEnumerator _enumerator =
        (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

    public List<OutputDevice> GetOutputDevices()
    {
        var result = new List<OutputDevice>();
        if (_enumerator.EnumAudioEndpoints(EDataFlow.Render, Native.DEVICE_STATE_ACTIVE, out var collection) != 0)
            return result;

        collection.GetCount(out int count);
        for (int i = 0; i < count; i++)
        {
            try
            {
                collection.Item(i, out var device);
                device.GetId(out string id);
                result.Add(new OutputDevice(id, GetFriendlyName(device) ?? "Unknown device"));
            }
            catch (COMException) { /* device vanished mid-enumeration */ }
        }
        return result;
    }

    public string? GetDefaultDeviceId()
    {
        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) != 0)
            return null;
        device.GetId(out string id);
        return id;
    }

    public DeviceHandle? OpenDevice(string id)
    {
        try
        {
            if (_enumerator.GetDevice(id, out var device) != 0) return null;
            return new DeviceHandle(id, device);
        }
        catch (COMException) { return null; }
    }

    private static string? GetFriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(0 /* STGM_READ */, out var store);
        var key = Native.PKEY_Device_FriendlyName;
        store.GetValue(ref key, out var value);
        try
        {
            return value.VarType == Native.VT_LPWSTR ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            Native.PropVariantClear(ref value);
        }
    }
}

/// <summary>One open output device: its master volume, meter and app sessions.</summary>
public sealed class DeviceHandle
{
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    private static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    private readonly IMMDevice _device;
    private readonly IAudioEndpointVolume _volume;
    private readonly IAudioMeterInformation _meter;

    public string Id { get; }

    internal DeviceHandle(string id, IMMDevice device)
    {
        Id = id;
        _device = device;
        _volume = Activate<IAudioEndpointVolume>(IID_IAudioEndpointVolume);
        _meter = Activate<IAudioMeterInformation>(IID_IAudioMeterInformation);
    }

    private T Activate<T>(Guid iid)
    {
        _device.Activate(ref iid, Native.CLSCTX_ALL, IntPtr.Zero, out object instance);
        return (T)instance;
    }

    public float Volume
    {
        get { _volume.GetMasterVolumeLevelScalar(out float v); return v; }
        set { var ctx = AudioContext.Id; _volume.SetMasterVolumeLevelScalar(Math.Clamp(value, 0f, 1f), ref ctx); }
    }

    public bool Muted
    {
        get { _volume.GetMute(out bool m); return m; }
        set { var ctx = AudioContext.Id; _volume.SetMute(value, ref ctx); }
    }

    public float Peak
    {
        get { _meter.GetPeakValue(out float p); return p; }
    }

    /// <summary>Lists every app session on this device that hasn't expired.</summary>
    public List<AudioSession> GetSessions()
    {
        var list = new List<AudioSession>();
        // A fresh session manager each time makes sure newly started apps show up.
        var manager = Activate<IAudioSessionManager2>(IID_IAudioSessionManager2);
        manager.GetSessionEnumerator(out var sessions);
        sessions.GetCount(out int count);

        for (int i = 0; i < count; i++)
        {
            try
            {
                sessions.GetSession(i, out object raw);
                if (raw is not IAudioSessionControl2 control) continue;

                control.GetState(out var state);
                if (state == AudioSessionState.Expired) continue;

                control.GetProcessId(out uint pid);
                bool isSystem = control.IsSystemSoundsSession() == 0;
                control.GetDisplayName(out string displayName);

                list.Add(new AudioSession(
                    pid, isSystem, state == AudioSessionState.Active, displayName,
                    (ISimpleAudioVolume)raw, (IAudioMeterInformation)raw));
            }
            catch (COMException) { /* session went away while we looked at it */ }
            catch (InvalidCastException) { }
        }
        return list;
    }
}

/// <summary>A single audio stream owned by an app.</summary>
public sealed class AudioSession
{
    private readonly ISimpleAudioVolume _volume;
    private readonly IAudioMeterInformation _meter;

    public uint ProcessId { get; }
    public bool IsSystemSounds { get; }
    public bool IsActive { get; }
    public string DisplayName { get; }

    internal AudioSession(uint pid, bool isSystem, bool isActive, string displayName,
        ISimpleAudioVolume volume, IAudioMeterInformation meter)
    {
        ProcessId = pid;
        IsSystemSounds = isSystem;
        IsActive = isActive;
        DisplayName = displayName ?? "";
        _volume = volume;
        _meter = meter;
    }

    public float Volume
    {
        get { _volume.GetMasterVolume(out float v); return v; }
        set { var ctx = AudioContext.Id; _volume.SetMasterVolume(Math.Clamp(value, 0f, 1f), ref ctx); }
    }

    public bool Muted
    {
        get { _volume.GetMute(out bool m); return m; }
        set { var ctx = AudioContext.Id; _volume.SetMute(value, ref ctx); }
    }

    public float Peak
    {
        get { _meter.GetPeakValue(out float p); return p; }
    }
}

internal static class AudioContext
{
    // Tags our volume changes so Windows knows they came from this app.
    public static readonly Guid Id = Guid.NewGuid();
}

internal static class IndirectString
{
    /// <summary>Turns "@%SystemRoot%\...dll,-202" style names into readable text.</summary>
    public static string Resolve(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('@')) return value;
        var sb = new StringBuilder(512);
        return Native.SHLoadIndirectString(value, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : "";
    }
}
