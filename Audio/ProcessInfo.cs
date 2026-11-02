using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppMixer.Audio.Interop;

namespace AppMixer.Audio;

/// <summary>What we know about the process behind an audio session.</summary>
public sealed class ProcessInfo
{
    /// <summary>Sessions with the same key are shown as one row (e.g. all Discord processes).</summary>
    public required string GroupKey { get; init; }
    public required string ProcessName { get; init; }
    public string? Description { get; init; }
    public ImageSource? Icon { get; init; }
}

/// <summary>Looks up process names and icons, and caches them by process id.</summary>
public sealed class ProcessInfoCache
{
    private readonly Dictionary<uint, ProcessInfo> _byPid = new();
    private readonly Dictionary<string, ImageSource?> _iconsByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <returns>null if the process is no longer running.</returns>
    public ProcessInfo? Get(uint pid)
    {
        if (_byPid.TryGetValue(pid, out var cached)) return cached;

        string? path = GetImagePath(pid, out bool gone);
        if (gone) return null;

        string processName;
        try { processName = Process.GetProcessById((int)pid).ProcessName; }
        catch (ArgumentException) { return null; } // exited
        catch (InvalidOperationException) { return null; }

        string? description = null;
        if (path != null)
        {
            try { description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim(); }
            catch { /* no version info */ }
        }

        var info = new ProcessInfo
        {
            GroupKey = path != null ? "exe:" + path.ToLowerInvariant() : "name:" + processName.ToLowerInvariant(),
            ProcessName = processName,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            Icon = path != null ? GetIcon(path) : null,
        };
        _byPid[pid] = info;
        return info;
    }

    /// <summary>Forget processes that no longer have audio sessions.</summary>
    public void Prune(HashSet<uint> seen)
    {
        foreach (var pid in _byPid.Keys.Where(p => !seen.Contains(p)).ToList())
            _byPid.Remove(pid);
    }

    private static string? GetImagePath(uint pid, out bool gone)
    {
        gone = false;
        IntPtr handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            gone = Marshal.GetLastWin32Error() == Native.ERROR_INVALID_PARAMETER;
            return null;
        }
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return Native.QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private ImageSource? GetIcon(string path)
    {
        if (_iconsByPath.TryGetValue(path, out var icon)) return icon;

        icon = null;
        var info = new Native.SHFILEINFO();
        Native.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
        if (info.hIcon != IntPtr.Zero)
        {
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                icon = bmp;
            }
            catch { /* bad icon */ }
            finally { Native.DestroyIcon(info.hIcon); }
        }
        _iconsByPath[path] = icon;
        return icon;
    }
}
