using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DesktopWidgets;

/// <summary>Month view opened by clicking the clock, styled like the widgets. Created on click and closed on focus loss.</summary>
internal sealed class CalendarWindow : Window
{
    private const double CellWidth = 40;
    private const double CellHeight = 36;
    private const double ShadowMargin = 22; // transparent room around the card for its drop shadow
    private const double Gap = 10;          // between the clock and the card

    private readonly WidgetWindow _anchor;
    private readonly ImageBrush _frost = new() { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
    private readonly Border _frostLayer = new() { CornerRadius = new CornerRadius(28) };
    private readonly Border _card = new()
    {
        CornerRadius = new CornerRadius(28),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(18, 14, 18, 18),
    };
    private readonly TextBlock _title = new()
    {
        FontSize = 19,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Cursor = Cursors.Hand,
    };
    private readonly UniformGrid _weekdays = new() { Columns = 7, Rows = 1, Height = 28 };
    private readonly UniformGrid _days = new() { Columns = 7, Rows = 6 };
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private bool _closing;

    public CalendarWindow(WidgetWindow anchor)
    {
        _anchor = anchor;

        Title = "Calendar";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");

        _title.MouseLeftButtonUp += (_, _) =>
        {
            _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            Render();
        };

        var header = new Grid { Height = 40, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(_title);
        header.Children.Add(NavButton("M5,0 L0,5 L5,10", -1, HorizontalAlignment.Left));
        header.Children.Add(NavButton("M0,0 L5,5 L0,10", 1, HorizontalAlignment.Right));

        _days.Width = CellWidth * 7;
        _days.Height = CellHeight * 6;
        _card.Child = new StackPanel { Children = { header, _weekdays, _days } };
        if (SettingsStore.Current.WidgetShadow)
            _frostLayer.Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.28, Direction = 270 };

        Content = new Grid
        {
            LayoutTransform = new ScaleTransform(anchor.Config.Scale, anchor.Config.Scale),
            Children = { new Grid { Margin = new Thickness(ShadowMargin), Children = { _frostLayer, _card } } },
        };

        MouseWheel += (_, e) => ShowMonth(e.Delta > 0 ? -1 : 1);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                CloseOnce();
        };
        Deactivated += (_, _) => CloseOnce();
        Closing += (_, _) => _closing = true;

        Loaded += (_, _) => UpdateLook();
        ContentRendered += (_, _) => UpdateLook();
        LocationChanged += (_, _) => UpdateLook();
        Backdrop.Changed += UpdateLook;
        Closed += (_, _) => Backdrop.Changed -= UpdateLook;
    }

    private void CloseOnce()
    {
        if (!_closing)
            Close();
    }

    /// <summary>Opens below the clock, or above it when there is no room, inside the monitor's work area.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetToolWindow(hwnd);
        if (_anchor.PillScreenRect() is not { } pill)
            return;

        var dpi = VisualTreeHelper.GetDpi(_anchor).DpiScaleX;
        var content = (UIElement)Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = content.DesiredSize;
        var width = (int)Math.Ceiling(size.Width * dpi);
        var height = (int)Math.Ceiling(size.Height * dpi);
        var margin = (int)Math.Round(ShadowMargin * _anchor.Config.Scale * dpi);
        var gap = (int)Math.Round(Gap * _anchor.Config.Scale * dpi);
        var work = Native.WorkAreaOf(new WindowInteropHelper(_anchor).Handle);

        var x = Math.Max(work.Left - margin, Math.Min(pill.Left - margin, work.Right + margin - width));
        var y = pill.Bottom + gap - margin;
        if (y + height - margin > work.Bottom)
            y = pill.Top - gap + margin - height;
        y = Math.Max(work.Top - margin, y);
        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    private Border NavButton(string chevron, int months, HorizontalAlignment side)
    {
        var button = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = Brushes.Transparent,
            HorizontalAlignment = side,
            Cursor = Cursors.Hand,
            Child = new Path
            {
                Data = Geometry.Parse(chevron),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ((Path)button.Child).SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = this });
        button.MouseEnter += (_, _) => button.Background = new SolidColorBrush(Color.FromArgb(0x26, TextColor.R, TextColor.G, TextColor.B));
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, _) => ShowMonth(months);
        return button;
    }

    private Color TextColor => ((SolidColorBrush)Foreground).Color;

    private void ShowMonth(int delta)
    {
        _month = _month.AddMonths(delta);
        Render();
    }

    /// <summary>Same layers as a widget: frosted wallpaper, the tint and the thin border.</summary>
    private void UpdateLook()
    {
        _card.BorderBrush = WidgetWindow.TintBorder();

        if (SettingsStore.Current.BlurAmount > 0 && Backdrop.Image is { } image && ScreenRect() is { } rect)
        {
            var area = Backdrop.Map(rect);
            _frost.ImageSource = image;
            _frost.Viewbox = area;
            _frostLayer.Background = _frost;
            _card.Background = WidgetWindow.TintBrush();
            Foreground = WidgetWindow.UsesDarkText(Backdrop.Luminance(area)) ? WidgetWindow.DarkText : WidgetWindow.LightText;
        }
        else
        {
            // The card can cover other windows, so without the frost it needs a solid background.
            _frostLayer.Background = (Brush)FindResource("Menu.Background");
            _card.Background = Brushes.Transparent;
            Foreground = (Brush)FindResource("Menu.Text");
        }
        Render();
    }

    private Native.RECT? ScreenRect()
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

    private void Render()
    {
        var culture = Strings.Culture;
        _title.Text = culture.TextInfo.ToTitleCase(_month.ToString("MMMM yyyy", culture));

        // The first day of the week follows the Windows region, not the UI language.
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        _weekdays.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            _weekdays.Children.Add(new TextBlock
            {
                Text = culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)firstDay + i) % 7))),
                FontSize = 13,
                Opacity = 0.65,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var offset = ((int)_month.DayOfWeek - (int)firstDay + 7) % 7;
        var day = _month.AddDays(-offset);
        _days.Children.Clear();
        for (var i = 0; i < 42; i++, day = day.AddDays(1))
            _days.Children.Add(DayCell(day));
    }

    private FrameworkElement DayCell(DateTime day)
    {
        var text = new TextBlock
        {
            Text = day.Day.ToString(CultureInfo.InvariantCulture),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (day.Month != _month.Month)
            text.Opacity = 0.35;
        if (day != DateTime.Today)
            return text;

        // Today: a filled circle in the text colour, with the number knocked out.
        text.FontWeight = FontWeights.SemiBold;
        text.Foreground = TextColor.R < 128 ? WidgetWindow.LightText : WidgetWindow.DarkText;
        return new Grid
        {
            Children =
            {
                new Ellipse { Width = 32, Height = 32, Fill = Foreground, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                text,
            },
        };
    }
}
