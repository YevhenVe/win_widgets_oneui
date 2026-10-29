using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DesktopWidgets;

/// <summary>
/// Notification-area icon built on Shell_NotifyIcon directly (no WinForms, which would
/// cost ~20 MB just for this), with a WPF context menu.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "DesktopWidgets";

    private const int CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private readonly HwndSource _window;
    private readonly IntPtr _hIcon;
    private readonly int _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly ContextMenu _menu = new() { Placement = PlacementMode.MousePoint };

    public TrayIcon()
    {
        // Hidden popup window that receives the icon's mouse callbacks.
        _window = new HwndSource(new HwndSourceParameters("DesktopWidgetsTray")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            Width = 0,
            Height = 0,
        });
        _window.AddHook(WndProc);
        _hIcon = DrawIcon();
        Add();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage && (int)lParam is WM_RBUTTONUP or WM_LBUTTONUP)
        {
            ShowMenu();
            handled = true;
        }
        else if (msg == _taskbarCreated)
        {
            Add(); // Explorer restarted
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        BuildMenu();
        App.StyleMenu(_menu);
        _menu.IsOpen = true;
        // Without foreground the menu would not close when clicking elsewhere.
        if (PresentationSource.FromVisual(_menu) is HwndSource popup)
            SetForegroundWindow(popup.Handle);
    }

    private void BuildMenu()
    {
        var settings = SettingsStore.Current;
        _menu.Items.Clear();
        Item(Strings.AddClock, () => App.Instance.AddWidget("clock"));
        Item(Strings.AddWeather, () => App.Instance.AddWidget("weather"));
        _menu.Items.Add(new Separator());

        var background = new MenuItem { Header = Strings.Background };
        foreach (var (mode, label) in new[] { (BlurMode.Blur, Strings.FrostedBlur), (BlurMode.None, Strings.NoBlur) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = settings.Blur == mode };
            item.Click += (_, _) => App.Instance.SetBlur(mode);
            background.Items.Add(item);
        }
        background.Items.Add(new Separator());
        background.Items.Add(BlurSlider(settings.BlurAmount));
        _menu.Items.Add(background);
        Item(Strings.RefreshBackground, () => Backdrop.Rebuild("tray menu"));
        Item(Strings.WidgetShadow, () => App.Instance.SetWidgetShadow(!settings.WidgetShadow), settings.WidgetShadow);

        var theme = new MenuItem { Header = Strings.Theme };
        foreach (var (value, label) in new[] { (Theme.Auto, Strings.ThemeAuto), (Theme.Light, Strings.ThemeLight), (Theme.Dark, Strings.ThemeDark) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = settings.Theme == value };
            item.Click += (_, _) => App.Instance.SetTheme(value);
            theme.Items.Add(item);
        }
        _menu.Items.Add(theme);

        // Language names stay in their own language so they can be found from either one.
        var language = new MenuItem { Header = Strings.LanguageMenu };
        foreach (var (value, label) in new[] { (Language.English, "English"), (Language.Ukrainian, "Українська") })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = Strings.Current == value };
            item.Click += (_, _) => App.Instance.SetLanguage(value);
            language.Items.Add(item);
        }
        _menu.Items.Add(language);

        Item(Strings.WeatherLocationMenu, App.Instance.AskCity);
        Item(Strings.UseFahrenheit, () =>
        {
            settings.Fahrenheit = !settings.Fahrenheit;
            SettingsStore.Save();
            App.Instance.RefreshWeather();
        }, settings.Fahrenheit);
        Item(Strings.StartWithWindows, () => SetAutostart(!IsAutostart()), IsAutostart());
        _menu.Items.Add(new Separator());
        Item(Strings.Exit, () => App.Instance.Shutdown());
    }

    /// <summary>"Blur amount" slider; the menu stays open while dragging and the widgets update live.</summary>
    private static MenuItem BlurSlider(int value)
    {
        var label = new TextBlock { Text = Strings.BlurAmount(value) };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = value,
            Width = 180,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            IsMoveToPointEnabled = true,
            Margin = new Thickness(0, 4, 0, 0),
        };
        slider.ValueChanged += (_, e) =>
        {
            label.Text = Strings.BlurAmount((int)e.NewValue);
            App.Instance.SetBlurAmount((int)e.NewValue);
        };
        return new MenuItem
        {
            Header = new StackPanel { Children = { label, slider } },
            StaysOpenOnClick = true,
            IsEnabled = SettingsStore.Current.Blur == BlurMode.Blur,
        };
    }

    private void Item(string header, Action action, bool? isChecked = null)
    {
        var item = new MenuItem { Header = header, IsCheckable = isChecked.HasValue, IsChecked = isChecked == true };
        item.Click += (_, _) => action();
        _menu.Items.Add(item);
    }

    private static bool IsAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    private static void SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable)
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    private void Add()
    {
        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _hIcon;
        data.szTip = "Desktop Widgets";
        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NOTIFYICONDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = 1,
    };

    /// <summary>Same motif as the widgets: white clock face on a blue disc.</summary>
    private static IntPtr DrawIcon()
    {
        const int size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x3F, 0x7F, 0xDB)), null, new Point(16, 16), 15, 15);
            dc.DrawEllipse(Brushes.White, null, new Point(16, 16), 9, 9);
            var hand = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), 2.4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            dc.DrawLine(hand, new Point(16, 16), new Point(16, 10));
            dc.DrawLine(hand, new Point(16, 16), new Point(21, 18));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var png = stream.ToArray();
        return CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, size, size, 0);
    }

    public void Dispose()
    {
        var data = NewData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        DestroyIcon(_hIcon);
        _window.Dispose();
    }

    private const uint NIM_ADD = 0, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] data, uint size, bool icon, uint version, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
