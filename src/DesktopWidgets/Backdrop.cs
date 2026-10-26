using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopWidgets;

/// <summary>
/// A blurred, downscaled copy of the whole desktop wallpaper (all monitors, in physical pixels).
/// Widgets live right above the desktop, so the wallpaper is exactly what is behind them —
/// sampling it gives a One UI-style frosted background with smooth, anti-aliased edges.
/// </summary>
internal static class Backdrop
{
    private const int Downscale = 4;
    private const double BlurRadius = 14; // in downscaled pixels, i.e. ~56 physical px
    private const int Padding = 24;

    private static readonly DispatcherTimer Debounce = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private static readonly DispatcherTimer Poll = new() { Interval = TimeSpan.FromMinutes(2) };
    private static string _signature = "";
    private static Int32Rect _virtual;
    private static byte[] _pixels = Array.Empty<byte>();
    private static int _stride;

    public static BitmapSource? Image { get; private set; }

    /// <summary>Raised on the UI thread after the backdrop image was rebuilt.</summary>
    public static event Action? Changed;

    public static void Initialize()
    {
        Debounce.Tick += (_, _) =>
        {
            Debounce.Stop();
            Rebuild();
        };
        // Slideshow / Spotlight change the wallpaper without always notifying, so also poll cheaply.
        Poll.Tick += (_, _) =>
        {
            if (ReadSignature() != _signature)
                Rebuild();
        };
        Poll.Start();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General)
                Schedule();
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => Schedule();
        Rebuild();
    }

    private static void Schedule() => Application.Current.Dispatcher.InvokeAsync(() =>
    {
        Debounce.Stop();
        Debounce.Start();
    });

    /// <summary>Maps a physical screen rectangle to the backdrop image's pixel space.</summary>
    public static Rect Map(Native.RECT r) => new(
        (double)(r.Left - _virtual.X) / Downscale,
        (double)(r.Top - _virtual.Y) / Downscale,
        Math.Max(1.0, (double)(r.Right - r.Left) / Downscale),
        Math.Max(1.0, (double)(r.Bottom - r.Top) / Downscale));

    /// <summary>Average relative luminance (0..1) of the backdrop inside an image-space rectangle.</summary>
    public static double Luminance(Rect area)
    {
        if (Image is null || _pixels.Length == 0)
            return 0.5;
        int x0 = Math.Clamp((int)area.Left, 0, Image.PixelWidth - 1), y0 = Math.Clamp((int)area.Top, 0, Image.PixelHeight - 1);
        int x1 = Math.Clamp((int)area.Right, x0 + 1, Image.PixelWidth), y1 = Math.Clamp((int)area.Bottom, y0 + 1, Image.PixelHeight);
        double sum = 0;
        var count = 0;
        for (var y = y0; y < y1; y += 2)
        for (var x = x0; x < x1; x += 2)
        {
            var i = y * _stride + x * 4; // BGRA
            sum += (0.0722 * _pixels[i] + 0.7152 * _pixels[i + 1] + 0.2126 * _pixels[i + 2]) / 255;
            count++;
        }
        return count == 0 ? 0.5 : sum / count;
    }

    private sealed record MonitorWallpaper(Native.RECT Bounds, string? Path);

    private static (List<MonitorWallpaper> Monitors, WallpaperPosition Position, Color Background) ReadDesktop()
    {
        var monitors = new List<MonitorWallpaper>();
        var position = WallpaperPosition.Fill;
        var background = Colors.Black;
        try
        {
            var api = (IDesktopWallpaper)new DesktopWallpaperClass();
            position = api.GetPosition();
            var bg = api.GetBackgroundColor(); // COLORREF 0x00BBGGRR
            background = Color.FromRgb((byte)bg, (byte)(bg >> 8), (byte)(bg >> 16));
            var count = api.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    var id = api.GetMonitorDevicePathAt(i);
                    var rect = api.GetMonitorRECT(id); // throws for disconnected monitors
                    monitors.Add(new MonitorWallpaper(rect, ResolveImage(api.GetWallpaper(id), (int)i)));
                }
                catch
                {
                    // inactive monitor
                }
            }
        }
        catch
        {
            // COM unavailable: fall back to the primary screen and the transcoded wallpaper.
        }

        if (monitors.Count == 0)
        {
            var r = new Native.RECT
            {
                Right = (int)SystemParameters.PrimaryScreenWidth,
                Bottom = (int)SystemParameters.PrimaryScreenHeight,
            };
            monitors.Add(new MonitorWallpaper(r, ResolveImage(null, 0)));
        }
        return (monitors, position, background);
    }

    private static string? ResolveImage(string? path, int index)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return path;
        // Windows keeps a copy of what is actually shown; it survives deleted originals and slideshows.
        var themes = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes");
        foreach (var candidate in new[] { Path.Combine(themes, $"Transcoded_{index:000}"), Path.Combine(themes, "TranscodedWallpaper") })
            if (File.Exists(candidate))
                return candidate;
        return null;
    }

    private static string ReadSignature()
    {
        var (monitors, position, background) = ReadDesktop();
        return string.Join("|", monitors.Select(m =>
            $"{m.Bounds.Left},{m.Bounds.Top},{m.Bounds.Right},{m.Bounds.Bottom}:{m.Path}:{(m.Path is null ? 0 : File.GetLastWriteTimeUtc(m.Path).Ticks)}"))
            + $"|{position}|{background}";
    }

    public static void Rebuild()
    {
        try
        {
            _signature = ReadSignature();
            var (monitors, position, background) = ReadDesktop();
            Image = Compose(monitors, position, background);
            _stride = Image.PixelWidth * 4;
            _pixels = new byte[_stride * Image.PixelHeight];
            Image.CopyPixels(_pixels, _stride, 0);
        }
        catch (Exception ex)
        {
            WidgetWindow.Log("backdrop failed: " + ex);
            Image = null;
            _pixels = Array.Empty<byte>();
        }
        Changed?.Invoke();
        MemoryTrim.Schedule();
    }

    private static BitmapSource Compose(List<MonitorWallpaper> monitors, WallpaperPosition position, Color background)
    {
        var left = monitors.Min(m => m.Bounds.Left);
        var top = monitors.Min(m => m.Bounds.Top);
        var right = monitors.Max(m => m.Bounds.Right);
        var bottom = monitors.Max(m => m.Bounds.Bottom);
        _virtual = new Int32Rect(left, top, right - left, bottom - top);

        var width = Math.Max(1, _virtual.Width / Downscale);
        var height = Math.Max(1, _virtual.Height / Downscale);
        Rect ToLocal(Native.RECT r) => new(
            (double)(r.Left - left) / Downscale, (double)(r.Top - top) / Downscale,
            (double)(r.Right - r.Left) / Downscale, (double)(r.Bottom - r.Top) / Downscale);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            if (position == WallpaperPosition.Span)
            {
                if (monitors.FirstOrDefault(m => m.Path != null)?.Path is { } span && Load(span) is { } img)
                    DrawFitted(dc, img, new Rect(0, 0, width, height), WallpaperPosition.Fill);
            }
            else
            {
                foreach (var m in monitors)
                {
                    if (m.Path is null || Load(m.Path) is not { } img)
                        continue;
                    var rect = ToLocal(m.Bounds);
                    dc.PushClip(new RectangleGeometry(rect));
                    DrawFitted(dc, img, rect, position);
                    dc.Pop();
                }
            }
        }

        var sharp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        sharp.Render(visual);
        sharp.Freeze();
        return Blur(sharp);
    }

    private static void DrawFitted(DrawingContext dc, BitmapSource img, Rect target, WallpaperPosition position)
    {
        double iw = img.PixelWidth, ih = img.PixelHeight;
        switch (position)
        {
            case WallpaperPosition.Stretch:
                dc.DrawImage(img, target);
                break;
            case WallpaperPosition.Tile:
                var brush = new ImageBrush(img)
                {
                    TileMode = TileMode.Tile,
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(target.X, target.Y, iw, ih),
                };
                dc.DrawRectangle(brush, null, target);
                break;
            case WallpaperPosition.Center:
                // Images are decoded at 1/Downscale, so their pixel size already matches the canvas.
                dc.DrawImage(img, Centered(target, iw, ih));
                break;
            default:
                var scale = position == WallpaperPosition.Fit
                    ? Math.Min(target.Width / iw, target.Height / ih)
                    : Math.Max(target.Width / iw, target.Height / ih);
                dc.DrawImage(img, Centered(target, iw * scale, ih * scale));
                break;
        }
    }

    private static Rect Centered(Rect target, double w, double h) =>
        new(target.X + (target.Width - w) / 2, target.Y + (target.Height - h) / 2, w, h);

    private static BitmapSource? Load(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path); // read fully so the file is never locked
            var probe = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(bytes);
            image.DecodePixelWidth = Math.Max(16, probe.PixelWidth / Downscale);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            WidgetWindow.Log($"wallpaper load failed {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Gaussian blur with edge extension, so screen borders don't fade to transparent.</summary>
    private static BitmapSource Blur(BitmapSource sharp)
    {
        int w = sharp.PixelWidth, h = sharp.PixelHeight;
        var host = new Grid
        {
            Width = w + Padding * 2,
            Height = h + Padding * 2,
            Background = new ImageBrush(sharp) { Stretch = Stretch.Fill },
            Effect = new BlurEffect { Radius = BlurRadius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        };
        host.Children.Add(new System.Windows.Controls.Image
        {
            Source = sharp,
            Width = w,
            Height = h,
            Stretch = Stretch.Fill,
            Margin = new Thickness(Padding),
        });
        var size = new Size(host.Width, host.Height);
        host.Measure(size);
        host.Arrange(new Rect(size));

        var blurred = new RenderTargetBitmap(w + Padding * 2, h + Padding * 2, 96, 96, PixelFormats.Pbgra32);
        blurred.Render(host);
        var cropped = new CroppedBitmap(blurred, new Int32Rect(Padding, Padding, w, h));
        var result = new WriteableBitmap(cropped);
        result.Freeze();
        return result;
    }
}
