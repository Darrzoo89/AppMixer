using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AppMixer;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\AppMixer.SingleInstance";
    private const string ShowEventName = @"Local\AppMixer.ShowWindow";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private MainWindow? _window;

    public static bool IsExiting { get; private set; }
    public TrayIcon? Tray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one copy at a time: a second launch just brings up the first one's window.
        _instanceMutex = new Mutex(true, InstanceMutexName, out bool isFirst);
        if (!isFirst)
        {
            AllowSetForegroundWindow(-1 /* ASFW_ANY */);
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            _instanceMutex = null;
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => Tray?.ShowWindow());
        }) { IsBackground = true }.Start();

        Resources["AccentBrush"] = new SolidColorBrush(ReadWindowsAccent());
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SessionEnding += (_, _) => IsExiting = true;

        _window = new MainWindow();
        Tray = new TrayIcon(_window, (ViewModels.MainViewModel)_window.DataContext, ExitApp);

        // "--tray" is used when starting with Windows: stay hidden in the tray.
        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
            _window.Show();
    }

    /// <summary>Really quit (from the tray menu).</summary>
    public void ExitApp()
    {
        IsExiting = true;
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray?.Dispose();
        _instanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Use the same accent color as Windows (and Game Bar).</summary>
    private static Color ReadWindowsAccent()
    {
        try
        {
            // The light shade from the accent palette is what Windows' own dark-mode sliders use.
            using var accent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (accent?.GetValue("AccentPalette") is byte[] palette && palette.Length >= 4)
                return Color.FromRgb(palette[0], palette[1], palette[2]);
        }
        catch { }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                byte r = (byte)(abgr & 0xFF), g = (byte)((abgr >> 8) & 0xFF), b = (byte)((abgr >> 16) & 0xFF);
                // Lighten very dark accents a bit so they stand out on the dark background.
                double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                if (lum < 0.45)
                {
                    double t = Math.Min(0.45, 0.45 - lum + 0.15);
                    r = (byte)(r + (255 - r) * t); g = (byte)(g + (255 - g) * t); b = (byte)(b + (255 - b) * t);
                }
                return Color.FromRgb(r, g, b);
            }
        }
        catch { }
        return Color.FromRgb(0x4C, 0xC2, 0xFF);
    }

    /// <summary>Scroll the mouse wheel over any slider to nudge it by 2.</summary>
    private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider slider) return;
        slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? 2 : -2), slider.Minimum, slider.Maximum);
        e.Handled = true;
    }
}
