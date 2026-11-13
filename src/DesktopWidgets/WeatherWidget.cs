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
    private readonly StackPanel _text = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(20) };
    private bool _loading;

    public WeatherWidget(WidgetConfig config) : base(config)
    {
        _icon.Effect = GlyphShadow();
        _text.Children.Add(_temp);
        _text.Children.Add(_city);
        SetPillContent(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _icon,
                _text,
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

    /// <summary>Opens the Windows Weather app, or the forecast on weather.com when the app is not installed.</summary>
    protected override void OnClick()
    {
        using (var app = Registry.ClassesRoot.OpenSubKey("msnweather"))
        {
            if (app is not null && Open("msnweather:"))
                return;
        }
        var s = SettingsStore.Current.LastWeather;
        var url = s is { Latitude: not 0 } or { Longitude: not 0 }
            ? string.Create(CultureInfo.InvariantCulture, $"https://weather.com/weather/today/l/{s.Latitude:0.####},{s.Longitude:0.####}")
            : "https://weather.com/";
        Open(url);
    }

    private static bool Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log($"open {target} failed: " + ex.Message);
            return false;
        }
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        var refresh = new MenuItem { Header = Strings.RefreshWeather };
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        var city = new MenuItem { Header = Strings.SetLocation };
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
                _city.Text = Strings.NoData;
            ToolTip = Strings.UpdateFailed(ex.Message);
        }
        finally
        {
            _loading = false;
            MemoryTrim.Schedule();
        }
    }

    /// <summary>Re-renders the cached weather after a language or unit switch (texts, temperature and tooltip).</summary>
    public void Rerender() => Render(SettingsStore.Current.LastWeather);

    private void Render(WeatherSnapshot? s)
    {
        if (s is null)
        {
            _temp.Text = "--°";
            _city.Text = Strings.Loading;
            _icon.Content = WeatherIcons.Create(0, true, 76);
            CentreText();
            return;
        }

        _temp.Text = $"{Math.Round(s.Display(SettingsStore.Current.Fahrenheit)):0}°";
        _city.Text = s.City;
        _icon.Content = WeatherIcons.Create(s.Code, s.IsDay, 76);
        ToolTip = Strings.Updated(s.FetchedAt);
        CentreText();
    }

    /// <summary>
    /// A text line keeps more empty room above the digits than below the city name, so the letters sat
    /// below the middle of the pill. Shifts the two lines (without changing the layout) until the letters
    /// themselves are centred, measured from the actual text.
    /// </summary>
    private void CentreText()
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        (double Height, Rect Ink) Measure(TextBlock block)
        {
            var text = new FormattedText(block.Text.Length > 0 ? block.Text : " ", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize, Brushes.White, dpi);
            return (text.Height, text.BuildGeometry(new Point(0, 0)).Bounds);
        }

        var temp = Measure(_temp);
        var city = Measure(_city);
        var cityTop = _temp.Margin.Top + temp.Height + _temp.Margin.Bottom;
        var height = cityTop + city.Height;
        var inkTop = _temp.Margin.Top + temp.Ink.Top;
        var inkBottom = city.Ink.IsEmpty ? _temp.Margin.Top + temp.Ink.Bottom : cityTop + city.Ink.Bottom;
        _text.RenderTransform = new TranslateTransform(0, Math.Round(height / 2 - (inkTop + inkBottom) / 2));
    }
}
