using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopWidgets;

/// <summary>
/// The PillsWidgets icon: a two-tone capsule (blue half with clock hands, white half with a sun) on a dark tile.
/// Drawn as vectors, so the tray, the About box and app.ico all render it sharp at their own size.
/// </summary>
public static class AppIcon
{
    private static Drawing? _drawing;

    /// <summary>The icon in a 256×256 box.</summary>
    public static Drawing Drawing => _drawing ??= Create();

    public static ImageSource Image => new DrawingImage(Drawing);

    private static Drawing Create()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRoundedRectangle(
                new LinearGradientBrush(Color.FromRgb(0x2C, 0x34, 0x40), Color.FromRgb(0x16, 0x1B, 0x22), new Point(0, 0), new Point(1, 1)),
                null, new Rect(8, 8, 240, 240), 56, 56);

            // The capsule, tilted like a pill.
            var capsule = new RectangleGeometry(new Rect(38, 88, 180, 80), 40, 40);
            dc.PushTransform(new RotateTransform(-40, 128, 128));
            dc.PushClip(capsule);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3F, 0x7F, 0xDB)), null, new Rect(38, 88, 90, 80));
            dc.DrawRectangle(Brushes.White, null, new Rect(128, 88, 90, 80));
            dc.Pop();
            dc.Pop();

            var hand = new Pen(Brushes.White, 3.85) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(hand, new Point(98, 158), new Point(98, 142.4));
            dc.DrawLine(hand, new Point(98, 158), new Point(110, 164));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x3C)), null, new Point(160, 98), 17, 17);
        }
        group.Freeze();
        return group;
    }

    /// <summary>The icon rendered at exactly <paramref name="px"/>×<paramref name="px"/> pixels.</summary>
    public static BitmapSource Render(int px)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(px / 256.0, px / 256.0));
            dc.DrawDrawing(Drawing);
        }
        var bitmap = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] Png(int px)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(px)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Writes a multi-size .ico (PNG frames). Used to make app.ico; not called by the app itself.</summary>
    public static void WriteIco(string path, params int[] sizes)
    {
        var frames = Array.ConvertAll(sizes, Png);
        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        w.Write((short)0);
        w.Write((short)1); // icon
        w.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(frames[i].Length);
            w.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var frame in frames)
            w.Write(frame);
    }
}
