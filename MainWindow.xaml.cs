using System.Windows;
using AppMixer.ViewModels;

namespace AppMixer;

public partial class MainWindow : Window
{
    private readonly Settings _settings = Settings.Load();
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(_settings.PlayingOnly);
        DataContext = _vm;
        ApplySavedBounds(_settings);
        Topmost = _settings.Pinned;

        StateChanged += (_, _) =>
        {
            // A borderless window overhangs the screen by a few pixels when maximized.
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            UpdateMeters();
        };
        // Live meters only run while you can see them.
        IsVisibleChanged += (_, _) => UpdateMeters();
        Closing += OnClosing;
    }

    private void UpdateMeters() => _vm.MetersEnabled = IsVisible && WindowState != WindowState.Minimized;

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        if (App.IsExiting) return;

        // The X button hides to the tray; "Exit" in the tray menu really quits.
        e.Cancel = true;
        Dispatcher.BeginInvoke(Hide); // can't hide while the close is still in progress
        if (!_settings.TrayHintShown)
        {
            _settings.TrayHintShown = true;
            _settings.Save();
            ((App)Application.Current).Tray?.ShowStillRunningHint();
        }
    }

    private void ApplySavedBounds(Settings s)
    {
        Width = Math.Max(MinWidth, s.Width);
        Height = Math.Max(MinHeight, s.Height);
        if (s.Left is double left && s.Top is double top &&
            left >= SystemParameters.VirtualScreenLeft - 50 &&
            top >= SystemParameters.VirtualScreenTop - 10 &&
            left + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            top + 50 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void SaveSettings()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;
        _settings.Width = bounds.Width;
        _settings.Height = bounds.Height;
        _settings.Pinned = Topmost;
        _settings.PlayingOnly = _vm.PlayingOnly;
        _settings.Save();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
