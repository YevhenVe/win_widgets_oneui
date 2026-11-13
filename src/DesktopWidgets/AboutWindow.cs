using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopWidgets;

/// <summary>About box from the tray menu. English only on purpose; colours follow the Windows light or dark mode like the menus.</summary>
public sealed class AboutWindow : Window
{
    public const string RepoUrl = "https://github.com/YevhenVe/win_widgets_oneui";

    private static AboutWindow? _open;

    /// <summary>Shows the box, or brings the open one to the front.</summary>
    public static void ShowOnce()
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }
        _open = new AboutWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    /// <summary>"0.8.1" from the build's informational version ("0.8.1+commit").</summary>
    public static string Version =>
        (typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
        .Split('+')[0];

    public AboutWindow()
    {
        Title = "About PillsWidgets";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;
        SetResourceReference(BackgroundProperty, "Menu.Background");
        SetResourceReference(ForegroundProperty, "Menu.Text");

        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, Width = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        ok.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(28, 26, 28, 22),
            Width = 340,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        Logo(56),
                        new StackPanel
                        {
                            Margin = new Thickness(16, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                new TextBlock { Text = "PillsWidgets", FontSize = 22, FontWeight = FontWeights.SemiBold },
                                Dimmed($"Version {Version}", new Thickness(0, 2, 0, 0)),
                            },
                        },
                    },
                },
                new TextBlock
                {
                    Text = "One UI-style clock and weather widgets for the Windows 11 desktop.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 20, 0, 0),
                },
                Links(new Thickness(0, 14, 0, 0), ("GitHub", RepoUrl), ("Releases", RepoUrl + "/releases"), ("Changelog", RepoUrl + "/blob/main/CHANGELOG.md")),
                Separator(),
                Credit("Weather data by ", "Open-Meteo.com", "https://open-meteo.com/"),
                Credit("Location by IP: ", "ipapi.co", "https://ipapi.co/", " and ", "ip-api.com", "https://ip-api.com/"),
                Dimmed($"© {DateTime.Now.Year} YevhenVe", new Thickness(0, 12, 0, 0)),
                ok,
            },
        };

        SourceInitialized += (_, _) => Native.SetDarkTitleBar(new WindowInteropHelper(this).Handle, IsDark());
    }

    private bool IsDark() => TryFindResource("Menu.Background") is SolidColorBrush { Color.R: < 128 };

    private static TextBlock Dimmed(string text, Thickness margin) => new() { Text = text, Opacity = 0.65, Margin = margin };

    private static Border Separator() => new() { Height = 1, Margin = new Thickness(0, 18, 0, 14), Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)) };

    private static TextBlock Links(Thickness margin, params (string Text, string Url)[] links)
    {
        var block = new TextBlock { Margin = margin };
        for (var i = 0; i < links.Length; i++)
        {
            if (i > 0)
                block.Inlines.Add(new Run("   ·   ") { Foreground = Brushes.Gray });
            block.Inlines.Add(Link(links[i].Text, links[i].Url));
        }
        return block;
    }

    /// <summary>Text with links: pairs of (plain text, link text, url), the last plain text optional.</summary>
    private static TextBlock Credit(params string[] parts)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4), Opacity = 0.85 };
        for (var i = 0; i < parts.Length; i += 3)
        {
            block.Inlines.Add(new Run(parts[i]));
            if (i + 2 < parts.Length)
                block.Inlines.Add(Link(parts[i + 1], parts[i + 2]));
        }
        return block;
    }

    private static Hyperlink Link(string text, string url)
    {
        var link = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url), Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF)), TextDecorations = null };
        link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.MouseLeave += (_, _) => link.TextDecorations = null;
        link.RequestNavigate += (_, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // No browser: nothing to do.
            }
            e.Handled = true;
        };
        return link;
    }

    private static FrameworkElement Logo(double size) => new Image { Source = AppIcon.Image, Width = size, Height = size };
}
