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

/// <summary>Replaced by <see cref="AppSettings.Tint"/>; only read from old settings files.</summary>
public enum Theme
{
    Auto,
    Light,
    Dark,
}

/// <summary>Manual override of the text colour the theme would pick.</summary>
public enum TextColor
{
    Auto, // follows the brightness of the tinted backdrop
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
    public int Tint { get; set; } // -50 (black) .. 0 (none) .. +50 (white), in percent

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Theme? Theme { get; set; }
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
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new();
                if (Current.Theme == Theme.Dark)
                    Current.Tint = -50; // the old Dark theme's plate
                Current.Theme = null;
            }
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
