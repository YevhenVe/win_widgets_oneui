using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DesktopWidgets;

/// <summary>Current weather from Open-Meteo (free, no API key). Location from the city setting or by IP.</summary>
public static class WeatherService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Dictionary<string, string> LocalizedNames = new();

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

        var (lat, lon, name) = await LocateByIpAsync();
        return (lat, lon, await LocalizeAsync(name, lat, lon));
    }

    private static async Task<(double Lat, double Lon, string City)> LocateByIpAsync()
    {
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

    /// <summary>
    /// IP services only return English city names. Looks the name up in the geocoder in the UI language
    /// and takes the match closest to the IP location (several towns can share a name); keeps the
    /// original when there is no translation or no match nearby.
    /// </summary>
    private static async Task<string> LocalizeAsync(string city, double lat, double lon)
    {
        if (city.Length == 0 || Strings.Current == Language.English)
            return city;
        var key = $"{Strings.Code}|{city}";
        if (LocalizedNames.TryGetValue(key, out var cached))
            return cached;

        var name = city;
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=10&language={Strings.Code}";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (doc.RootElement.TryGetProperty("results", out var results))
            {
                var best = double.MaxValue;
                foreach (var r in results.EnumerateArray())
                {
                    var km = DistanceKm(lat, lon, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble());
                    if (km < MaxMatchKm && km < best)
                    {
                        best = km;
                        name = r.GetProperty("name").GetString() ?? city;
                    }
                }
            }
        }
        catch
        {
            return city; // try again on the next refresh
        }
        LocalizedNames[key] = name;
        return name;
    }

    private const double MaxMatchKm = 50; // IP services often name the metro centre, tens of km away

    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * rad / 2), 2) +
                Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Pow(Math.Sin((lon2 - lon1) * rad / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(a));
    }

    public static async Task<(double Lat, double Lon, string City)> GeocodeAsync(string city)
    {
                var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city.Trim())}&count=1&language={Strings.Code}";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            throw new InvalidOperationException(Strings.CityNotFound(city));
        var r = results[0];
        return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(),
            r.GetProperty("name").GetString() ?? city);
    }
}
