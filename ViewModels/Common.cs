using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AppMixer.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

internal static class VolumeGlyphs
{
    // Segoe MDL2 Assets speaker icons
    public static string For(double volume, bool muted) =>
        muted ? ""
        : volume <= 0.5 ? ""
        : volume < 34 ? ""
        : volume < 67 ? ""
        : "";
}
