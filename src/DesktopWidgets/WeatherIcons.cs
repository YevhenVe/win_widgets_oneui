using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DesktopWidgets;

/// <summary>Flat One UI–style weather glyphs drawn on a 100×100 canvas.</summary>
public static class WeatherIcons
{
    private enum Kind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunder }

    // WMO weather interpretation codes, see https://open-meteo.com/en/docs
    private static Kind Classify(int code) => code switch
    {
        0 => Kind.Clear,
        1 or 2 => Kind.PartlyCloudy,
        3 => Kind.Cloudy,
        45 or 48 => Kind.Fog,
        >= 51 and <= 57 => Kind.Drizzle,
        (>= 61 and <= 67) or (>= 80 and <= 82) => Kind.Rain,
        (>= 71 and <= 77) or 85 or 86 => Kind.Snow,
        >= 95 => Kind.Thunder,
        _ => Kind.Cloudy,
    };

    public static FrameworkElement Create(int code, bool isDay, double size)
    {
        var c = new Canvas { Width = 100, Height = 100 };
        switch (Classify(code))
        {
            case Kind.Clear:
                if (isDay) Sun(c, 50, 50, 34); else Moon(c, 50, 50, 32);
                break;
            case Kind.PartlyCloudy:
                if (isDay) Sun(c, 38, 38, 24); else Moon(c, 38, 38, 22);
                Cloud(c, 12, 14, 0.9, White(0xF5));
                break;
            case Kind.Cloudy:
                Cloud(c, 18, -6, 0.75, White(0x99));
                Cloud(c, 0, 8, 1, White(0xF5));
                break;
            case Kind.Fog:
                for (var i = 0; i < 4; i++)
                    Bar(c, 18 + (i % 2) * 8, 30 + i * 13, 64 - (i % 2) * 8, White(0xE0));
                break;
            case Kind.Drizzle:
                Cloud(c, 0, -4, 1, White(0xF5));
                Drops(c, 2);
                break;
            case Kind.Rain:
                Cloud(c, 0, -4, 1, White(0xF5));
                Drops(c, 3);
                break;
            case Kind.Snow:
                Cloud(c, 0, -4, 1, White(0xF5));
                foreach (var x in new[] { 34.0, 50, 66 })
                    Dot(c, x, 86 + (x == 50 ? 6 : 0), 5, Brushes.White);
                break;
            case Kind.Thunder:
                Cloud(c, 0, -4, 1, new SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE8)));
                c.Children.Add(new Polygon
                {
                    Points = new PointCollection { new(56, 66), new(42, 86), new(52, 86), new(45, 102), new(64, 80), new(54, 80), new(60, 66) },
                    Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xD3, 0x4A)),
                });
                break;
        }
        return new Viewbox { Width = size, Height = size, Child = c };
    }

    private static Brush White(byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));

    private static void Sun(Canvas c, double x, double y, double r)
    {
        Dot(c, x, y, r * 1.22, new SolidColorBrush(Color.FromArgb(0xB0, 0xE3, 0xBE, 0x5E)));
        Dot(c, x, y, r, new SolidColorBrush(Color.FromRgb(0xFF, 0xCD, 0x45)));
    }

    private static void Moon(Canvas c, double x, double y, double r)
    {
        var crescent = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(x, y), r, r),
            new EllipseGeometry(new Point(x + r * 0.55, y - r * 0.35), r * 0.85, r * 0.85));
        c.Children.Add(new Path { Data = crescent, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xC2)) });
    }

    private static void Cloud(Canvas c, double dx, double dy, double scale, Brush fill)
    {
        // One path with Nonzero fill so overlapping lobes keep a uniform alpha.
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new EllipseGeometry(new Point(32, 62), 17, 17));
        group.Children.Add(new EllipseGeometry(new Point(54, 50), 23, 23));
        group.Children.Add(new EllipseGeometry(new Point(74, 63), 15, 15));
        group.Children.Add(new RectangleGeometry(new Rect(32, 56, 42, 22), 4, 4));
        var t = new TransformGroup();
        t.Children.Add(new ScaleTransform(scale, scale, 50, 60));
        t.Children.Add(new TranslateTransform(dx, dy));
        group.Transform = t;
        c.Children.Add(new Path { Data = group, Fill = fill });
    }

    private static void Drops(Canvas c, int count)
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x9C, 0xCB, 0xFF));
        var xs = count == 2 ? new[] { 42.0, 60 } : new[] { 36.0, 52, 68 };
        foreach (var x in xs)
            c.Children.Add(new Line
            {
                X1 = x, Y1 = 82, X2 = x - 5, Y2 = 96,
                Stroke = brush, StrokeThickness = 5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
    }

    private static void Bar(Canvas c, double x, double y, double width, Brush fill)
    {
        var bar = new Rectangle { Width = width, Height = 7, RadiusX = 3.5, RadiusY = 3.5, Fill = fill };
        Canvas.SetLeft(bar, x);
        Canvas.SetTop(bar, y);
        c.Children.Add(bar);
    }

    private static void Dot(Canvas c, double x, double y, double r, Brush fill)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill };
        Canvas.SetLeft(e, x - r);
        Canvas.SetTop(e, y - r);
        c.Children.Add(e);
    }
}
