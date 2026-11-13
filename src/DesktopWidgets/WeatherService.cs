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
        var (lat, lon, city) = await ResolveLocationAsync(settings);
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
            $"&current=temperature_2m,weather_code,is_day&temperature_unit=celsius&timezone=auto");

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var current = doc.RootElement.GetProperty("current");
        return new WeatherSnapshot(
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("weather_code").GetInt32(),
            current.GetProperty("is_day").GetInt32() == 1,
            city,
            DateTime.Now,
            lat,
            lon,
            Celsius: true);
    }

    /// <summary>
    /// A typed city is looked up once and kept as a place id with coordinates, so switching the UI language
    /// can't lose it or move it to a namesake; only its name is fetched again, in the current language.
    /// </summary>
    private static async Task<(double Lat, double Lon, string City)> ResolveLocationAsync(AppSettings settings)
    {
        if (settings.City is { } city && !string.IsNullOrWhiteSpace(city))
        {
            var place = settings.CityPlace is { } saved && saved.Query == city ? saved : await FindPlaceAsync(city);
            settings.CityPlace = place;
            return (place.Latitude, place.Longitude, await PlaceNameAsync(place.Id) ?? city);
        }

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
        if (city.Length == 0 || Strings.Code == "en")
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

    /// <summary>
    /// The geocoder only matches names in the requested language (and English), so "Харків" is not found in
    /// Russian and "Харьков" in Ukrainian finds another town. Tries the UI language, English and every installed
    /// language, and prefers a result whose name is exactly what was typed.
    /// </summary>
    private static async Task<CityPlace> FindPlaceAsync(string city)
    {
        var query = city.Trim();
        var languages = new List<string> { Strings.Code, "en" };
        foreach (var lang in Strings.Available)
        {
            if (!languages.Contains(lang.Code))
                languages.Add(lang.Code);
        }

        CityPlace? first = null;
        foreach (var lang in languages)
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=1&language={lang}";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                continue;
            var r = results[0];
            var place = new CityPlace(city, r.GetProperty("id").GetInt32(), r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble());
            if (string.Equals(r.GetProperty("name").GetString(), query, StringComparison.CurrentCultureIgnoreCase))
                return place;
            first ??= place;
        }
        return first ?? throw new InvalidOperationException(Strings.CityNotFound(city));
    }

    /// <summary>The place's name in the UI language, or null to show the city as typed.</summary>
    private static async Task<string?> PlaceNameAsync(int id)
    {
        var key = $"{Strings.Code}|#{id}";
        if (LocalizedNames.TryGetValue(key, out var cached))
            return cached;
        try
        {
            var url = string.Create(CultureInfo.InvariantCulture, $"https://geocoding-api.open-meteo.com/v1/get?id={id}&language={Strings.Code}");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (doc.RootElement.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } text)
                return LocalizedNames[key] = text;
        }
        catch
        {
            // Try again on the next refresh.
        }
        return null;
    }
}
