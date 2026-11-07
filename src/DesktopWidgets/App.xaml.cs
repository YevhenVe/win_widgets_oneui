using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace DesktopWidgets;

public partial class App : Application
{
    public static App Instance => (App)Current;

    private readonly List<WidgetWindow> _widgets = new();
    private Mutex? _singleInstance;
    private TrayIcon? _tray;
    private bool _darkMenus;
    private Native.WinEventProc? _foregroundChanged; // kept alive: Windows calls it
    private IntPtr _foregroundHook;
    private readonly System.Windows.Threading.DispatcherTimer _lowerTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private IntPtr _locationDialog;

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
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category == UserPreferenceCategory.General)
                Dispatcher.InvokeAsync(SyncMenuTheme);
        };
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

        // Win+D raises the desktop, and the widgets with it as it owns them; when another window comes
        // to the front again the desktop drops back, so put the widgets back just above it each time.
        // The desktop drops back only after the foreground event, so check a moment later.
        _lowerTimer.Tick += (_, _) =>
        {
            _lowerTimer.Stop();
            KeepWidgetsOnDesktop();
        };
        _foregroundChanged = (_, _, _, _, _, _, _) => ScheduleKeepOnDesktop();
        _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _foregroundChanged, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        _lowerTimer.Start();

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
        if (_foregroundHook != IntPtr.Zero)
            Native.UnhookWinEvent(_foregroundHook);
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
        widget.Remove();
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

    public void SetTint(int tint)
    {
        SettingsStore.Current.Tint = tint;
        SettingsStore.Save();
        foreach (var w in _widgets)
            w.UpdateBackdrop();
    }

    /// <summary>Follows a switch of the Windows mode. Widget menus are recreated, as closed menus miss the change.</summary>
    private void SyncMenuTheme()
    {
        if (WindowsUsesDarkApps() == _darkMenus)
            return;
        ApplyMenuTheme();
        foreach (var w in _widgets)
            w.ResetMenu();
        if (_locationDialog != IntPtr.Zero)
            Native.SetDarkTitleBar(_locationDialog, _darkMenus);
    }

    private static bool WindowsUsesDarkApps()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is 0;
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

    /// <summary>Menu colours (Menus.xaml) follow the Windows app mode: dark #2A2A2A or Windows 11 light.</summary>
    private void ApplyMenuTheme()
    {
        var dark = _darkMenus = WindowsUsesDarkApps();
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

    public void SetTextColor(TextColor color)
    {
        SettingsStore.Current.TextColor = color;
        SettingsStore.Save();
        foreach (var w in _widgets)
            w.UpdateBackdrop();
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
        dialog.SetResourceReference(Control.BackgroundProperty, "Menu.Background");
        dialog.SetResourceReference(Control.ForegroundProperty, "Menu.Text");
        dialog.SourceInitialized += (_, _) =>
        {
            _locationDialog = new System.Windows.Interop.WindowInteropHelper(dialog).Handle;
            Native.SetDarkTitleBar(_locationDialog, _darkMenus);
        };
        dialog.Closed += (_, _) => _locationDialog = IntPtr.Zero;
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

    public void ScheduleKeepOnDesktop()
    {
        _lowerTimer.Stop();
        _lowerTimer.Start();
    }

    private void KeepWidgetsOnDesktop()
    {
        // While Win+D shows the desktop it is the foreground window; moving any window would cancel it.
        // A focused widget (its menu is open) is left alone too.
        var foreground = Native.GetForegroundWindow();
        var handles = _widgets.Select(w => w.Handle).ToHashSet();
        if (Native.IsDesktop(foreground) || handles.Contains(foreground))
            return;
        // Only move widgets that are not already in the run of widgets right above the desktop.
        var onDesktop = new HashSet<IntPtr>();
        for (var h = Native.GetWindow(Native.FindWindow("Progman", null), Native.GW_HWNDPREV);
             h != IntPtr.Zero && handles.Contains(h);
             h = Native.GetWindow(h, Native.GW_HWNDPREV))
            onDesktop.Add(h);
        foreach (var w in _widgets)
        {
            if (!onDesktop.Contains(w.Handle))
                w.LowerToDesktop();
        }
    }

    /// <summary>
    /// Explorer restarted: the desktop window is new, so widgets are attached to it again, and any
    /// widget Windows destroyed together with the old desktop is opened again.
    /// </summary>
    public async void OnExplorerRestarted()
    {
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2)); // let the desktop window appear
        foreach (var widget in _widgets.ToList())
        {
            if (widget.IsGone)
            {
                _widgets.Remove(widget);
                Open(widget.Config);
            }
            else
            {
                widget.AttachToDesktop();
            }
        }
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
