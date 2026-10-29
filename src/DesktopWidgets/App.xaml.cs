using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopWidgets;

public partial class App : Application
{
    public static App Instance => (App)Current;

    private readonly List<WidgetWindow> _widgets = new();
    private Mutex? _singleInstance;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "DesktopWidgets.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        // Two small, mostly static windows don't need a Direct3D device; the GPU driver
        // alone costs tens of MB of private memory.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        WidgetWindow.Preview = e.Args.Contains("--preview");
        SettingsStore.Load();
        ApplyMenuTheme();
        Backdrop.Initialize();

        if (SettingsStore.Current.Widgets.Count == 0)
        {
            var area = SystemParameters.WorkArea;
            SettingsStore.Current.Widgets.Add(new WidgetConfig { Type = "clock", Left = area.Right - 340, Top = area.Top + 40 });
            SettingsStore.Current.Widgets.Add(new WidgetConfig { Type = "weather", Left = area.Right - 340, Top = area.Top + 172 });
            SettingsStore.Save();
        }

        foreach (var config in SettingsStore.Current.Widgets)
            Open(config);

        _tray = new TrayIcon();

        if (WidgetWindow.Preview)
        {
            var diag = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            diag.Tick += (_, _) =>
            {
                using var p = System.Diagnostics.Process.GetCurrentProcess();
                WidgetWindow.Log($"diag managed={GC.GetTotalMemory(false) / 1048576.0:N1}MB private={p.PrivateMemorySize64 / 1048576}MB " +
                    $"gc0={GC.CollectionCount(0)} gc2={GC.CollectionCount(2)} backdropUpdates={WidgetWindow.BackdropUpdates} cpu={p.TotalProcessorTime.TotalSeconds:N2}s");
            };
            diag.Start();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_widgets.Count > 0)
            SettingsStore.Save();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    public void AddWidget(string type)
    {
        var area = SystemParameters.WorkArea;
        var config = new WidgetConfig
        {
            Type = type,
            Left = area.Left + area.Width / 2 - 120,
            Top = area.Top + area.Height / 2 - 60,
        };
        SettingsStore.Current.Widgets.Add(config);
        SettingsStore.Save();
        Open(config);
    }

    public void RemoveWidget(WidgetWindow widget)
    {
        SettingsStore.Current.Widgets.Remove(widget.Config);
        SettingsStore.Save();
        _widgets.Remove(widget);
        widget.Close();
    }

    public void SetBlur(BlurMode mode)
    {
        SettingsStore.Current.Blur = mode;
        SettingsStore.Save();
        foreach (var w in _widgets)
            w.UpdateBackdrop();
    }

    public void SetBlurAmount(int amount)
    {
        SettingsStore.Current.BlurAmount = amount;
        SettingsStore.Save();
        Backdrop.BlurAmountChanged();
    }

    public void SetWidgetShadow(bool enabled)
    {
        SettingsStore.Current.WidgetShadow = enabled;
        SettingsStore.Save();
        foreach (var w in _widgets)
            w.ApplyShadow();
    }

    public void SetTheme(Theme theme)
    {
        SettingsStore.Current.Theme = theme;
        SettingsStore.Save();
        ApplyMenuTheme();
        foreach (var w in _widgets)
            w.UpdateBackdrop();
    }

    public void SetLanguage(Language language)
    {
        SettingsStore.Current.Language = language;
        SettingsStore.Save();
        foreach (var w in _widgets)
        {
            if (w is ClockWidget clock)
                clock.Refresh();
            else if (w is WeatherWidget weather)
                weather.Rerender();
        }
        // City names come from the geocoder in the UI language, so fetch them again.
        RefreshWeather();
    }

    /// <summary>Menu colours (Menus.xaml): dark #2A2A2A with the Dark theme, Windows 11 light otherwise.</summary>
    private void ApplyMenuTheme()
    {
        var dark = SettingsStore.Current.Theme == Theme.Dark;
        Resources["Menu.Background"] = Brush(dark ? 0xFF2A2A2A : 0xFFF9F9F9);
        Resources["Menu.Border"] = Brush(dark ? 0xFF3C3C3C : 0xFFE0E0E0);
        Resources["Menu.Highlight"] = Brush(dark ? 0xFF3D3D3D : 0xFFEAEAEA);
        Resources["Menu.Text"] = Brush(dark ? 0xFFF2F2F2 : 0xFF1A1A1A);
        Resources["Menu.DisabledText"] = Brush(dark ? 0xFF7A7A7A : 0xFFA0A0A0);
        Resources["Menu.Separator"] = Brush(dark ? 0xFF424242 : 0xFFE5E5E5);
    }

    private static System.Windows.Media.SolidColorBrush Brush(uint argb)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    public async void RefreshWeather()
    {
        foreach (var w in _widgets.OfType<WeatherWidget>().ToList())
            await w.RefreshAsync();
    }

    public void AskCity()
    {
        var box = new TextBox { Text = SettingsStore.Current.City ?? "", Margin = new Thickness(0, 8, 0, 12), Padding = new Thickness(4) };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var auto = new Button { Content = Strings.AutoIp, Width = 90 };
        var dialog = new Window
        {
            Title = Strings.WeatherLocationTitle,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Width = 300,
                Children =
                {
                    new TextBlock { Text = Strings.CityPrompt, TextWrapping = TextWrapping.Wrap },
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, auto } },
                },
            },
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        auto.Click += (_, _) =>
        {
            box.Text = "";
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) =>
        {
            box.SelectAll();
            Keyboard.Focus(box);
        };

        if (dialog.ShowDialog() != true)
            return;
        SettingsStore.Current.City = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
        SettingsStore.Save();
        RefreshWeather();
    }

    private void Open(WidgetConfig config)
    {
        WidgetWindow widget = config.Type switch
        {
            "weather" => new WeatherWidget(config),
            _ => new ClockWidget(config),
        };
        _widgets.Add(widget);
        widget.Show();
    }
}
