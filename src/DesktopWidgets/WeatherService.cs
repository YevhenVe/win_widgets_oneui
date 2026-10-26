using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DesktopWidgets;

/// <summary>Current weather from Open-Meteo (free, no API key). Location from the city setting or by IP.</summary>
public static class WeatherService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopWidgets/0.1");
        return client;
    }

    public static async Task<WeatherSnapshot> FetchAsync(AppSettings settings)
    {
        var (lat, lon, city) = await ResolveLocationAsync(settings.City);
        var unit = settings.Fahrenheit ? "fahrenheit" : "celsius";
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
            $"&current=temperature_2m,weather_code,is_day&temperature_unit={unit}&timezone=auto");

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var current = doc.RootElement.GetProperty("current");
        return new WeatherSnapshot(
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("weather_code").GetInt32(),
            current.GetProperty("is_day").GetInt32() == 1,
            city,
            DateTime.Now,
            lat,
            lon);
    }

    private static async Task<(double Lat, double Lon, string City)> ResolveLocationAsync(string? city)
    {
        if (!string.IsNullOrWhiteSpace(city))
            return await GeocodeAsync(city);

        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipapi.co/json/"));
            var root = doc.RootElement;
            return (root.GetProperty("latitude").GetDouble(), root.GetProperty("longitude").GetDouble(),
                root.GetProperty("city").GetString() ?? "");
        }
        catch
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("http://ip-api.com/json/"));
            var root = doc.RootElement;
            return (root.GetProperty("lat").GetDouble(), root.GetProperty("lon").GetDouble(),
                root.GetProperty("city").GetString() ?? "");
        }
    }

    public static async Task<(double Lat, double Lon, string City)> GeocodeAsync(string city)
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city.Trim())}&count=1&language={lang}";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            throw new InvalidOperationException($"City \"{city}\" not found");
        var r = results[0];
        return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(),
            r.GetProperty("name").GetString() ?? city);
    }
}
