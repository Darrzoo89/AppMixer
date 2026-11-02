using System.IO;
using System.Text.Json;

namespace AppMixer;

/// <summary>Window position and toggles, saved to %AppData%\AppMixer\settings.json.</summary>
public sealed class Settings
{
    public bool Pinned { get; set; }
    public bool PlayingOnly { get; set; }
    public bool TrayHintShown { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 640;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppMixer", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* corrupt file: start fresh */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* not fatal */ }
    }
}
