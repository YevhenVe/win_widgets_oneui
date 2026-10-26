using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopWidgets;

public sealed class WeatherWidget : WidgetWindow
{
    private readonly ContentControl _icon = new() { Width = 76, Height = 76, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _temp = new()
    {
        FontSize = 40,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, -4, 0, -2),
    };
    private readonly TextBlock _city = new()
    {
        FontSize = 19,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = 110,
    };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(20) };
    private bool _loading;

    public WeatherWidget(WidgetConfig config) : base(config)
    {
        _icon.Effect = GlyphShadow();
        SetPillContent(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _icon,
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(22, 0, 0, 0),
                    Children = { _temp, _city },
                },
            },
        });

        Render(SettingsStore.Current.LastWeather);

        _refresh.Tick += async (_, _) => await RefreshAsync();
        _refresh.Start();
        Loaded += async (_, _) => await RefreshAsync();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Closed += (_, _) =>
        {
            _refresh.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        };
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // Network is usually not up right after resume; give it a moment.
        if (e.Mode == PowerModes.Resume)
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                await RefreshAsync();
            });
    }

    /// <summary>Opens the full forecast for the same place on weather.com (Samsung Weather's data provider).</summary>
    protected override void OnClick()
    {
        var s = SettingsStore.Current.LastWeather;
        var url = s is { Latitude: not 0 } or { Longitude: not 0 }
            ? string.Create(CultureInfo.InvariantCulture, $"https://weather.com/weather/today/l/{s.Latitude:0.####},{s.Longitude:0.####}")
            : "https://weather.com/";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("open weather site failed: " + ex.Message);
        }
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        var refresh = new MenuItem { Header = "Refresh weather" };
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        var city = new MenuItem { Header = "Set location…" };
        city.Click += (_, _) => App.Instance.AskCity();
        menu.Items.Add(city);
    }

    public async Task RefreshAsync()
    {
        if (_loading)
            return;
        _loading = true;
        try
        {
            var snapshot = await WeatherService.FetchAsync(SettingsStore.Current);
            SettingsStore.Current.LastWeather = snapshot;
            SettingsStore.Save();
            Render(snapshot);
        }
        catch (Exception ex)
        {
            if (SettingsStore.Current.LastWeather is null)
                _city.Text = "No data";
            ToolTip = "Update failed: " + ex.Message;
        }
        finally
        {
            _loading = false;
            MemoryTrim.Schedule();
        }
    }

    private void Render(WeatherSnapshot? s)
    {
        if (s is null)
        {
            _temp.Text = "--°";
            _city.Text = "Loading…";
            _icon.Content = WeatherIcons.Create(0, true, 76);
            return;
        }

        _temp.Text = $"{Math.Round(s.Temperature):0}°";
        _city.Text = s.City;
        _icon.Content = WeatherIcons.Create(s.Code, s.IsDay, 76);
        ToolTip = $"Updated {s.FetchedAt:t}";
    }
}
