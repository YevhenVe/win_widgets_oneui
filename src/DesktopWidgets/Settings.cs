using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopWidgets;

public sealed class WidgetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "clock";
    public double Left { get; set; }
    public double Top { get; set; }
    public double Scale { get; set; } = 1.0;
    public bool Locked { get; set; }
}

public sealed record WeatherSnapshot(double Temperature, int Code, bool IsDay, string City, DateTime FetchedAt,
    double Latitude = 0, double Longitude = 0);

public enum Theme
{
    Auto,  // light glass; text colour follows the wallpaper brightness
    Light, // light glass, dark text
    Dark,  // dark plate, white text
}

/// <summary>Manual override of the text colour the theme would pick.</summary>
public enum TextColor
{
    Auto, // as the theme decides
    Dark,
    White,
}

public enum Language
{
    English,
    Ukrainian,
}

public sealed class AppSettings
{
    public List<WidgetConfig> Widgets { get; set; } = new();
    public string? City { get; set; }
    public bool Fahrenheit { get; set; }
    public BlurMode Blur { get; set; } = BlurMode.Blur;
    public int BlurAmount { get; set; } = 35; // 0..100
    public bool WidgetShadow { get; set; } = true;
    public Theme Theme { get; set; } = Theme.Auto;
    public TextColor TextColor { get; set; } = TextColor.Auto;
    public Language? Language { get; set; } // null: follow the Windows display language
    public WeatherSnapshot? LastWeather { get; set; }
}

public static class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopWidgets");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch
        {
            Current = new();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // Settings are best-effort; never crash the widget over them.
        }
    }
}
