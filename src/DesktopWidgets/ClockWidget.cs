using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DesktopWidgets;

/// <summary>Analog "cookie" clock + day and date, One UI style.</summary>
public sealed class ClockWidget : WidgetWindow
{
    private const double Face = 80;

    private readonly RotateTransform _hour = new(0, Face / 2, Face / 2);
    private readonly RotateTransform _minute = new(0, Face / 2, Face / 2);
    private readonly RotateTransform _second = new(0, Face / 2, Face / 2);
    private readonly TextBlock _day = new();
    private readonly TextBlock _date = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);

    public ClockWidget(WidgetConfig config) : base(config)
    {
        foreach (var tb in new[] { _day, _date })
        {
            tb.FontSize = 27;
            tb.FontWeight = FontWeights.SemiBold;
            tb.LineHeight = 34;
        }

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 2),
            Children = { _day, _date },
        };

        SetPillContent(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { BuildFace(), text },
        });

        _timer.Tick += (_, _) => Tick();
        Tick();
        Closed += (_, _) => _timer.Stop();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        _second.Angle = now.Second * 6;
        _minute.Angle = now.Minute * 6 + now.Second * 0.1;
        _hour.Angle = (now.Hour % 12) * 30 + now.Minute * 0.5;

        var culture = CultureInfo.CurrentCulture;
        var day = now.ToString("ddd", culture);
        _day.Text = culture.TextInfo.ToTitleCase(day.TrimEnd('.')) + ",";
        _date.Text = now.ToString("d MMM", culture).TrimEnd('.');

        // Re-arm exactly on the next second boundary so the hand never drifts.
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(1000 - now.Millisecond + 5);
        _timer.Start();
    }

    private FrameworkElement BuildFace()
    {
        const double c = Face / 2;
        var canvas = new Canvas { Width = Face, Height = Face, VerticalAlignment = VerticalAlignment.Center };

        canvas.Children.Add(new Path
        {
            Data = CookieGeometry(Face, lobes: 7, depth: 0.03),
            Fill = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFC)),
            Effect = GlyphShadow(),
        });

        canvas.Children.Add(Hand(c * 0.68, 6.5, Color.FromRgb(0xB4, 0xB4, 0xB8), _minute));
        canvas.Children.Add(Hand(c * 0.44, 7, Color.FromRgb(0x4A, 0x4A, 0x4E), _hour));
        canvas.Children.Add(Hand(c * 0.80, 1.4, Color.FromRgb(0x10, 0x10, 0x10), _second, tail: c * 0.16));

        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)),
            StrokeThickness = 2,
        };
        Canvas.SetLeft(dot, c - 3.5);
        Canvas.SetTop(dot, c - 3.5);
        canvas.Children.Add(dot);
        return canvas;
    }

    private static Line Hand(double length, double thickness, Color color, RotateTransform rotation, double tail = 0) => new()
    {
        X1 = Face / 2,
        Y1 = Face / 2 + tail,
        X2 = Face / 2,
        Y2 = Face / 2 - length,
        Stroke = new SolidColorBrush(color),
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        RenderTransform = rotation,
    };

    /// <summary>Soft rounded polygon ("cookie") like the One UI / Material You clock.</summary>
    private static Geometry CookieGeometry(double size, int lobes, double depth)
    {
        var c = size / 2;
        var r = c / (1 + depth);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            const int steps = 240;
            for (var i = 0; i <= steps; i++)
            {
                var t = i * 2 * Math.PI / steps;
                var rr = r * (1 + depth * Math.Cos(lobes * t));
                var p = new Point(c + rr * Math.Sin(t), c - rr * Math.Cos(t));
                if (i == 0)
                    ctx.BeginFigure(p, isFilled: true, isClosed: true);
                else
                    ctx.LineTo(p, isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();
        return geometry;
    }
}
