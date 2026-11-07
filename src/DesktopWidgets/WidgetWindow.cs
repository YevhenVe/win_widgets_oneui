using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DesktopWidgets;

/// <summary>
/// Borderless, frosted, pill-shaped window that lives on the desktop layer.
/// Subclasses only provide the content.
/// </summary>
public abstract class WidgetWindow : Window
{
    public const double PillHeight = 112;
    public const double PillWidth = 260; // every widget has the same footprint
    private const double ShadowMargin = 22; // transparent room around the pill for its drop shadow
    private const double MinScale = 0.5;
    private const double MaxScale = 3.0;

    /// <summary>Dev aid (--preview): show widgets above all windows instead of on the desktop layer.</summary>
    public static bool Preview { get; set; }

    public WidgetConfig Config { get; }

    private readonly ScaleTransform _scale = new(1, 1);
    private readonly ImageBrush _frost = new() { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
    private readonly Border _frostLayer = new();
    private readonly Border _pill = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private IntPtr _hwnd;
    private bool _removing;

    /// <summary>True once the window is gone, also when Windows destroyed it with its owner (Explorer restart).</summary>
    public bool IsGone { get; private set; }

    protected WidgetWindow(WidgetConfig config)
    {
        Config = config;

        Title = "Desktop Widget";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = config.Left;
        Top = config.Top;
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");

        var radius = new CornerRadius(PillHeight / 2);
        _frostLayer.CornerRadius = radius;
        _pill.Height = PillHeight;
        _pill.Width = PillWidth;
        _pill.CornerRadius = radius;
        _pill.BorderThickness = new Thickness(1);
        _pill.Padding = new Thickness(20, 0, 36, 0);

        // The margin sits inside the scale transform so the shadow room grows with the widget.
        Content = new Grid
        {
            LayoutTransform = _scale,
            Children = { new Grid { Margin = new Thickness(ShadowMargin), Children = { _frostLayer, _pill } } },
        };
        ApplyScale(config.Scale);
        ApplyShadow();

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SettingsStore.Save();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed)
                return;
            // DragMove blocks until the button is released; if the widget didn't move it was a click.
            var (left, top) = (Left, Top);
            if (!Config.Locked)
                DragMove();
            if (Math.Abs(Left - left) < 2 && Math.Abs(Top - top) < 2)
                OnClick();
        };
        LocationChanged += (_, _) =>
        {
            Config.Left = Left;
            Config.Top = Top;
            QueueSave();
            UpdateBackdrop();
        };
        PreviewMouseWheel += OnMouseWheel;
        SizeChanged += (_, _) => UpdateBackdrop();
        Backdrop.Changed += UpdateBackdrop;
        Closed += (_, _) =>
        {
            IsGone = true;
            Backdrop.Changed -= UpdateBackdrop;
        };

        ResetMenu();
        // A context menu closes at once on a window without focus, so the widget has it while the menu is open.
        ContextMenuOpening += (_, _) =>
        {
            Native.SetNoActivate(_hwnd, false);
            Native.SetForegroundWindow(_hwnd);
        };
    }

    protected void SetPillContent(UIElement content) => _pill.Child = content;

    protected void SetPillPadding(Thickness padding) => _pill.Padding = padding;

    /// <summary>
    /// A closed context menu is outside any visual tree and misses theme colour changes,
    /// so a fresh one is created whenever the theme switches.
    /// </summary>
    public void ResetMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => BuildMenu(menu);
        menu.Closed += (_, _) =>
        {
            Native.SetNoActivate(_hwnd, true);
            App.Instance.ScheduleKeepOnDesktop(); // opening the menu raised the widget
        };
        ContextMenu = menu;
    }

    protected virtual void AddMenuItems(ContextMenu menu) { }

    /// <summary>
    /// Makes the desktop (Progman) the widget's owner. Show desktop (Win+D) raises Progman above all
    /// windows, and Windows keeps owned windows above their owner, so the widget comes up with it.
    /// </summary>
    public void AttachToDesktop()
    {
        if (_hwnd == IntPtr.Zero || Preview)
            return;
        var progman = Native.FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
            Native.SetOwner(_hwnd, progman);
    }

    internal IntPtr Handle => _hwnd;

    /// <summary>Puts the widget back just above the desktop (the WndProc hook turns HWND_BOTTOM into that).</summary>
    public void LowerToDesktop()
    {
        if (_hwnd != IntPtr.Zero && !Preview && !IsGone)
            Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Closes the widget for good; any other close request (such as Alt+F4) is ignored.</summary>
    public void Remove()
    {
        _removing = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !_removing; // application shutdown ignores this
        base.OnClosing(e);
    }

    /// <summary>The soft drop shadow every widget glyph (clock face, weather icon) gets.</summary>
    protected static Effect GlyphShadow() =>
        new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1.5, Opacity = 0.18, Direction = 270 };

    /// <summary>Soft shadow under the whole pill, toggled from the tray menu.</summary>
    public void ApplyShadow() => _frostLayer.Effect = SettingsStore.Current.WidgetShadow
        ? new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.28, Direction = 270 }
        : null;

    /// <summary>Left click that did not turn into a drag.</summary>
    protected virtual void OnClick() { }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        Native.SetToolWindow(_hwnd);
        Native.SetNoActivate(_hwnd, true);
        EnsureOnScreen();
        UpdateBackdrop();
        if (Preview)
        {
            Topmost = true;
            return;
        }
        AttachToDesktop();
        Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    internal static int BackdropUpdates;

    public void UpdateBackdrop()
    {
        if (_hwnd == IntPtr.Zero)
            return;
        BackdropUpdates++;

        var luminance = 0.25;
        var frosted = false;
        if (Backdrop.Image is { } image && PillScreenRect() is { } rect)
        {
            var area = Backdrop.Map(rect);
            luminance = Backdrop.Luminance(area);
            if (SettingsStore.Current.BlurAmount > 0)
            {
                _frost.ImageSource = image;
                _frost.Viewbox = area;
                frosted = true;
            }
        }
        _frostLayer.Background = frosted ? _frost : ClickableClear;

        _pill.Background = TintBrush();
        _pill.BorderBrush = TintBorder();

        // Text inherits Window.Foreground, so every widget uses the same colour.
        Foreground = UsesDarkText(luminance) ? DarkText : LightText;
    }

    /// <summary>Whether text on a backdrop of this luminance is dark: the manual setting, or what is readable once tinted.</summary>
    internal static bool UsesDarkText(double luminance) => SettingsStore.Current.TextColor switch
    {
        TextColor.Dark => true,
        TextColor.White => false,
        _ => luminance + (TintLuminance - luminance) * Math.Abs(Tint) / 100.0 > 0.5,
    };

    private static int Tint => Math.Clamp(SettingsStore.Current.Tint, -50, 50);
    private static double TintLuminance => Tint < 0 ? 0.11 : 1.0;

    /// <summary>The translucent layer over the frost: near-black or white, by the tint slider.</summary>
    internal static Brush TintBrush()
    {
        var alpha = (byte)Math.Round(Math.Abs(Tint) * 2.55);
        return Frozen(Tint < 0 ? Color.FromArgb(alpha, 0x1C, 0x1C, 0x1F) : Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));
    }

    /// <summary>The thin white border, fainter towards black so it doesn't glow against the dark tint.</summary>
    internal static Brush TintBorder() =>
        Frozen(Color.FromArgb((byte)(Tint < 0 ? 0x40 + (0x1F - 0x40) * -Tint / 50 : 0x40), 0xFF, 0xFF, 0xFF));

    /// <summary>The pill's bounds in physical screen pixels (the window itself is larger because of the shadow room).</summary>
    internal Native.RECT? PillScreenRect()
    {
        if (_frostLayer.ActualWidth <= 0 || PresentationSource.FromVisual(_frostLayer) is null)
            return null;
        var topLeft = _frostLayer.PointToScreen(new Point(0, 0));
        var bottomRight = _frostLayer.PointToScreen(new Point(_frostLayer.ActualWidth, _frostLayer.ActualHeight));
        return new Native.RECT
        {
            Left = (int)Math.Round(topLeft.X),
            Top = (int)Math.Round(topLeft.Y),
            Right = (int)Math.Round(bottomRight.X),
            Bottom = (int)Math.Round(bottomRight.Y),
        };
    }

    // Invisible, but Windows passes clicks through pixels with zero alpha.
    private static readonly Brush ClickableClear = Frozen(Color.FromArgb(0x01, 0x00, 0x00, 0x00));
    internal static readonly Brush DarkText = Frozen(Color.FromRgb(0x10, 0x10, 0x14));
    internal static readonly Brush LightText = Frozen(Colors.White);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public void ApplyScale(double scale)
    {
        scale = Math.Round(Math.Clamp(scale, MinScale, MaxScale), 2);
        Config.Scale = scale;
        _scale.ScaleX = _scale.ScaleY = scale;
        QueueSave();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || Config.Locked)
            return;
        ApplyScale(Config.Scale + (e.Delta > 0 ? 0.05 : -0.05));
        e.Handled = true;
    }

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    internal static void Log(string message)
    {
        if (!Preview)
            return;
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DesktopWidgets.log");
        System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
    }

    private void EnsureOnScreen()
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;
        if (Left + 40 > right || Top + 40 > bottom || Left + ActualWidth < left + 40 || Top < top - 20)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - 320;
            Top = area.Top + 40;
        }
    }

    /// <summary>
    /// Keeps the widget glued right above the desktop: when Windows tries to raise it (e.g. for its
    /// context menu) or send it to the very bottom, it goes just above Progman instead. Other moves
    /// are left alone: Windows uses them to keep the widget above its owner, the desktop, on Win+D.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_WINDOWPOSCHANGING && !Preview)
        {
            var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
            if ((pos.flags & Native.SWP_NOZORDER) == 0 && (pos.hwndInsertAfter == IntPtr.Zero || pos.hwndInsertAfter == Native.HWND_BOTTOM))
            {
                var progman = Native.FindWindow("Progman", null);
                var above = progman == IntPtr.Zero ? IntPtr.Zero : Native.GetWindow(progman, Native.GW_HWNDPREV);
                if (above == hwnd)
                    pos.flags |= Native.SWP_NOZORDER;
                else
                    pos.hwndInsertAfter = above == IntPtr.Zero ? Native.HWND_BOTTOM : above;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    private void BuildMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        AddMenuItems(menu);
        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());

        var scaleMenu = new MenuItem { Header = Strings.Scale(Config.Scale) };
        foreach (var s in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 2.5 })
        {
            var item = new MenuItem { Header = s.ToString("P0", Strings.Culture), IsCheckable = true, IsChecked = Math.Abs(Config.Scale - s) < 0.001 };
            item.Click += (_, _) => ApplyScale(s);
            scaleMenu.Items.Add(item);
        }
        menu.Items.Add(scaleMenu);

        var lockItem = new MenuItem { Header = Strings.LockPosition, IsCheckable = true, IsChecked = Config.Locked };
        lockItem.Click += (_, _) =>
        {
            Config.Locked = lockItem.IsChecked;
            QueueSave();
        };
        menu.Items.Add(lockItem);

        menu.Items.Add(new Separator());
        var remove = new MenuItem { Header = Strings.RemoveWidget };
        remove.Click += (_, _) => App.Instance.RemoveWidget(this);
        menu.Items.Add(remove);
        var exit = new MenuItem { Header = Strings.Exit };
        exit.Click += (_, _) => App.Instance.Shutdown();
        menu.Items.Add(exit);
    }
}
