using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DesktopWidgets;

/// <summary>A language the tray menu can offer: its file code (en, uk, …) and its name in that language.</summary>
public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// UI text from language files. English and Ukrainian are built in; any <c>languages\xx.json</c> next to the
/// exe adds a language or replaces a built-in one. A missing string falls back to English.
/// </summary>
public static class Strings
{
    private const string Fallback = "en";
    private static readonly string[] BuiltIn = { "en", "uk" };
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "languages");
    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Pack?> Packs = new(StringComparer.OrdinalIgnoreCase);
    private static List<LanguageInfo>? _available;

    private sealed record Pack(string Name, CultureInfo Culture, Dictionary<string, string> Text);

    /// <summary>Built-in languages first, then the ones found in the languages folder, by name. Read again by <see cref="Rescan"/>.</summary>
    public static IReadOnlyList<LanguageInfo> Available
    {
        get
        {
            lock (Gate)
            {
                if (_available is null)
                {
                    var files = Directory.Exists(Folder)
                        ? Directory.GetFiles(Folder, "*.json").Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
                        : Enumerable.Empty<string>();
                    var extra = files.Except(BuiltIn).Select(Info).OfType<LanguageInfo>()
                        .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase);
                    _available = BuiltIn.Select(Info).OfType<LanguageInfo>().Concat(extra).ToList();
                }
                return _available;
            }
        }
    }

    private static LanguageInfo? Info(string code) => Load(code) is { } pack ? new LanguageInfo(code, pack.Name) : null;

    /// <summary>Forgets loaded files, so languages added or edited while the app runs show up in the menu.</summary>
    public static void Rescan()
    {
        lock (Gate)
        {
            Packs.Clear();
            _available = null;
        }
    }

    /// <summary>
    /// The saved language when its file exists, else the Windows display language when there is a file for it, else English.
    /// Settings from 0.7.x and older stored names (English, Ukrainian), which map to the new codes.
    /// </summary>
    public static string CurrentCode
    {
        get
        {
            var saved = SettingsStore.Current.Language switch
            {
                "English" => "en",
                "Ukrainian" => "uk",
                "Kharkiv" => "ru",
                var code => code,
            };
            if (saved is not null && Load(saved) is not null)
                return saved.ToLowerInvariant();
            var windows = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Load(windows) is not null ? windows : Fallback;
        }
    }

    /// <summary>Culture for dates and numbers, so the clock's day and month match the menus.</summary>
    public static CultureInfo Culture => Load(CurrentCode)?.Culture ?? CultureInfo.GetCultureInfo("en-US");

    /// <summary>Language code for the geocoding API (city names).</summary>
    public static string Code => Culture.TwoLetterISOLanguageName;

    private static Pack? Load(string code)
    {
        lock (Gate)
        {
            if (Packs.TryGetValue(code, out var cached))
                return cached;
            var pack = Parse(ReadFile(code));
            Packs[code] = pack;
            return pack;
        }
    }

    /// <summary>The languages folder wins over the built-in copy, so a built-in language can be corrected without a new build.</summary>
    private static string? ReadFile(string code)
    {
        if (code.Length == 0 || code.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        try
        {
            var path = Path.Combine(Folder, code + ".json");
            if (File.Exists(path))
                return File.ReadAllText(path);
        }
        catch (Exception)
        {
            // Unreadable file: use the built-in copy, if any.
        }
        using var stream = typeof(Strings).Assembly.GetManifestResourceStream($"lang.{code.ToLowerInvariant()}.json");
        return stream is null ? null : new StreamReader(stream).ReadToEnd();
    }

    private static Pack? Parse(string? json)
    {
        if (json is null)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json, JsonOptions);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
                return null;
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(root.TryGetProperty("culture", out var c) ? c.GetString() ?? "en-US" : "en-US");
            }
            catch (CultureNotFoundException)
            {
                culture = CultureInfo.GetCultureInfo("en-US");
            }
            var text = new Dictionary<string, string>();
            if (root.TryGetProperty("strings", out var strings) && strings.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in strings.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String)
                        text[p.Name] = p.Value.GetString()!;
                }
            }
            return new Pack(name.Trim(), culture, text);
        }
        catch (JsonException)
        {
            return null; // a broken file is skipped rather than breaking the menu
        }
    }

    /// <summary>The string in the current language, else English, else the key itself.</summary>
    private static string S([CallerMemberName] string key = "")
    {
        if (Load(CurrentCode)?.Text.TryGetValue(key, out var text) == true)
            return text;
        return Load(Fallback)?.Text.TryGetValue(key, out var english) == true ? english : key;
    }

    /// <summary>A string with {0} filled in; a translation with a broken placeholder falls back to English.</summary>
    private static string F(string key, object value)
    {
        try
        {
            return string.Format(Culture, S(key), value);
        }
        catch (FormatException)
        {
            var english = Load(Fallback)?.Text.GetValueOrDefault(key) ?? key;
            try
            {
                return string.Format(Culture, english, value);
            }
            catch (FormatException)
            {
                return english;
            }
        }
    }

    // Tray menu
    public static string AddClock => S();
    public static string AddWeather => S();
    public static string Background => S();
    public static string BlurAmount(int percent) => percent == 0 ? S("BlurOff") : F("BlurAmount", percent);
    public static string RefreshBackground => S();
    public static string WidgetShadow => S();
    public static string Tint(int percent) => percent switch
    {
        0 => S("TintNone"),
        < 0 => F("TintBlack", -percent),
        _ => F("TintWhite", percent),
    };
    public static string TextColorHeader => S();
    public static string TextAuto => S();
    public static string TextDark => S();
    public static string TextWhite => S();
    public static string LanguageMenu => S();
    public static string WeatherLocationMenu => S();
    public static string UseFahrenheit => S();
    public static string StartWithWindows => S();
    public static string AutostartOff => S();
    public static string AutostartShortcut => S();
    public static string AutostartRegistry => S();
    public static string AutostartTask => S();
    public static string Exit => S();

    // Widget menu
    public static string RefreshWeather => S();
    public static string SetLocation => S();
    public static string Scale(double scale) => F("Scale", scale.ToString("P0", Culture));
    public static string ShortDate => S();
    public static string DateTextSize(int size) => F("DateTextSize", size);
    public static string LockPosition => S();
    public static string RemoveWidget => S();

    // Location dialog
    public static string WeatherLocationTitle => S();
    public static string CityPrompt => S();
    public static string AutoIp => S();

    // Weather widget
    public static string Loading => S();
    public static string NoData => S();
    public static string Updated(DateTime time) => F("Updated", time.ToString("t", Culture));
    public static string UpdateFailed(string error) => F("UpdateFailed", error);
    public static string CityNotFound(string city) => F("CityNotFound", city);
}
