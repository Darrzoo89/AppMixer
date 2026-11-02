using System.ComponentModel;
using System.Windows;
using AppMixer.ViewModels;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace AppMixer;

/// <summary>
/// The icon in the system tray.
/// Left-click shows/hides the mixer, middle-click mutes, right-click opens the menu.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "AppMixer";

    private readonly MainWindow _window;
    private readonly MainViewModel _vm;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _statusItem, _muteItem, _topItem, _startupItem;

    public TrayIcon(MainWindow window, MainViewModel vm, Action exit)
    {
        _window = window;
        _vm = vm;

        _statusItem = new Forms.ToolStripMenuItem { Enabled = false };
        var openItem = new Forms.ToolStripMenuItem("Open mixer", null, (_, _) => ShowWindow())
        {
            Font = new Drawing.Font(Forms.SystemInformation.MenuFont, Drawing.FontStyle.Bold),
        };
        _muteItem = new Forms.ToolStripMenuItem("Mute all sound", null, (_, _) => _vm.MasterMuted = !_vm.MasterMuted);
        _topItem = new Forms.ToolStripMenuItem("Keep on top", null, (_, _) => _window.Topmost = !_window.Topmost);
        _startupItem = new Forms.ToolStripMenuItem("Start with Windows", null, (_, _) => SetStartWithWindows(!StartsWithWindows));
        var exitItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) => exit());

        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(AccentColor()),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Forms.Padding(2, 4, 2, 4),
        };
        menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            _statusItem, new Forms.ToolStripSeparator(),
            openItem, _muteItem, _topItem, _startupItem,
            new Forms.ToolStripSeparator(), exitItem,
        });
        menu.Opening += (_, _) => RefreshMenu();

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ToggleWindow();
            else if (e.Button == Forms.MouseButtons.Middle) _vm.MasterMuted = !_vm.MasterMuted;
        };

        _vm.PropertyChanged += OnViewModelChanged;
        UpdateTooltip();
    }

    public void ShowWindow()
    {
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ToggleWindow()
    {
        if (_window.IsVisible && _window.WindowState != WindowState.Minimized) _window.Hide();
        else ShowWindow();
    }

    /// <summary>One-time hint the first time the window is closed to the tray.</summary>
    public void ShowStillRunningHint() =>
        _icon.ShowBalloonTip(4000, "Volume Mixer is still running",
            "Click the tray icon to open it again. Right-click it to exit.", Forms.ToolTipIcon.None);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.MasterVolume) or nameof(MainViewModel.MasterMuted))
            UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        string text = _vm.MasterMuted ? "Volume Mixer – muted" : $"Volume Mixer – {_vm.MasterVolume:0}%";
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    private void RefreshMenu()
    {
        string device = _vm.SelectedDevice?.Name ?? "No output device";
        if (device.Length > 40) device = device[..39] + "…";
        _statusItem.Text = _vm.MasterMuted ? $"{device} · muted" : $"{device} · {_vm.MasterVolume:0}%";
        _muteItem.Checked = _vm.MasterMuted;
        _topItem.Checked = _window.Topmost;
        _startupItem.Checked = StartsWithWindows;
    }

    // ---------- Start with Windows (per-user Run key, starts hidden in the tray) ----------

    private static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
    }

    private static void SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enable && Environment.ProcessPath is string exe)
                key.SetValue(RunValueName, $"\"{exe}\" --tray");
            else
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch { /* registry blocked: leave as is */ }
    }

    // ---------- Helpers ----------

    private static Drawing.Icon LoadIcon()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
        return stream != null
            ? new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize)
            : Drawing.SystemIcons.Application;
    }

    private static Drawing.Color AccentColor()
    {
        if (Application.Current.Resources["AccentBrush"] is System.Windows.Media.SolidColorBrush b)
            return Drawing.Color.FromArgb(b.Color.R, b.Color.G, b.Color.B);
        return Drawing.Color.FromArgb(76, 194, 255);
    }

    public void Dispose()
    {
        _vm.PropertyChanged -= OnViewModelChanged;
        _icon.Visible = false; // otherwise a "ghost" icon lingers until you hover the tray
        _icon.Dispose();
    }
}

/// <summary>Dark theme for the tray's right-click menu, matching the mixer window.</summary>
internal sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    private static readonly Drawing.Color Text = Drawing.Color.FromArgb(243, 243, 243);
    private static readonly Drawing.Color DisabledText = Drawing.Color.FromArgb(150, 150, 150);
    private readonly Drawing.Color _accent;

    public DarkMenuRenderer(Drawing.Color accent) : base(new DarkColors())
    {
        _accent = accent;
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : DisabledText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        // Draw our own check mark (the default one is black and vanishes on a dark menu).
        var r = e.ImageRectangle;
        var g = e.Graphics;
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Drawing.Pen(_accent, Math.Max(2f, r.Height / 8f))
        {
            StartCap = Drawing.Drawing2D.LineCap.Round,
            EndCap = Drawing.Drawing2D.LineCap.Round,
            LineJoin = Drawing.Drawing2D.LineJoin.Round,
        };
        g.DrawLines(pen, new[]
        {
            new Drawing.PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
            new Drawing.PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
            new Drawing.PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.3f),
        });
    }

    private sealed class DarkColors : Forms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Bg = Drawing.Color.FromArgb(43, 43, 43);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(58, 58, 58);
        private static readonly Drawing.Color Line = Drawing.Color.FromArgb(64, 64, 64);

        public override Drawing.Color ToolStripDropDownBackground => Bg;
        public override Drawing.Color ImageMarginGradientBegin => Bg;
        public override Drawing.Color ImageMarginGradientMiddle => Bg;
        public override Drawing.Color ImageMarginGradientEnd => Bg;
        public override Drawing.Color MenuBorder => Line;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color SeparatorDark => Line;
        public override Drawing.Color SeparatorLight => Bg;
        public override Drawing.Color CheckBackground => Bg;
        public override Drawing.Color CheckSelectedBackground => Hover;
        public override Drawing.Color CheckPressedBackground => Hover;
        public override Drawing.Color ButtonSelectedBorder => Hover;
    }
}
