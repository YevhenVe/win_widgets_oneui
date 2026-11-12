# win_widgets_oneui

Samsung One UI–style clock and weather widgets for the Windows 11 desktop: frosted, translucent pills that sit right on top of your wallpaper, can be dragged anywhere and scaled freely.

![Clock and weather widgets](docs/screenshot.png)

## Features

- **Analog clock** with a One UI "cookie" face, plus the full day and month in your language. Right-click the clock for the text size and a short format; text that doesn't fit ends with an ellipsis.
- **Current weather** from [Open-Meteo](https://open-meteo.com/) (no API key needed). Location is detected by IP or set by city name. Click the widget to open the Windows Weather app (weather.com if the app is not installed).
- **Frosted glass look**: the wallpaper behind each widget is blurred with adjustable strength (0 turns it off), edges are smooth, multiple monitors and every wallpaper fit mode are supported. New wallpapers are picked up in about a second.
- **Soft shadow** under the widgets, can be turned off.
- **Lives on the desktop**: widgets stay below your windows and are hidden from the taskbar and Alt+Tab.
- **Calendar**: click the clock to open a month view; arrows or the mouse wheel change the month.
- **Move and scale**: drag to move, <kbd>Ctrl</kbd> + mouse wheel to resize (50–300 %). Positions and sizes are remembered.
- **Tint**: one slider from black 50 % through fully transparent (default) to white 50 %.
- **Menus** follow the Windows light or dark mode.
- **Text colour**: picked automatically for the tinted background, or set to dark or white manually.
- **Languages**: English and Ukrainian (Українська) built in, switched live from the tray; the date follows the chosen language. More languages can be added as files (see [Languages](#languages)).
- **Start with Windows** three ways: a Startup folder shortcut, the registry Run key or Task Scheduler. If one doesn't work on your PC (some antivirus programs block one of them), pick another.
- **Lightweight**: about 0 % CPU and ~45 MB RAM while idle.

## Install

1. Download the latest zip from [Releases](https://github.com/YevhenVe/win_widgets_oneui/releases):
   - `DesktopWidgets-<version>-win-x64.zip`: self-contained, runs on any Windows 10/11 x64.
   - `DesktopWidgets-<version>-win-x64-net10-runtime.zip`: tiny build for machines with the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Unzip anywhere, e.g. `%LOCALAPPDATA%\Programs\DesktopWidgets`, and run `DesktopWidgets.exe`.
3. The app isn't code-signed, so SmartScreen may warn on first launch: **More info → Run anyway**.

## Usage

| Where | What |
|---|---|
| Drag a widget | Move it |
| <kbd>Ctrl</kbd> + wheel over a widget | Resize it |
| Click the clock | Open the calendar (arrows or wheel: change month, click the title: back to today, Esc or click elsewhere: close) |
| Click the weather widget | Open the Windows Weather app (weather.com if it is not installed) |
| Right-click a widget | Clock: text size, short format. Weather: refresh, set location. All: scale, lock position, remove widget |
| Tray icon | Add clock / weather, background (blur, tint, text colour), refresh background, widget shadow, language, weather location, Fahrenheit, start with Windows (off / shortcut / registry / Task Scheduler), exit |

Settings are stored in `%LOCALAPPDATA%\DesktopWidgets\settings.json`.

## Languages

Each language is a JSON file in a `languages` folder next to `DesktopWidgets.exe`. The tray menu lists every file it finds there, each under its own `name`; new files show up the next time you open the menu. English and Ukrainian are also built into the exe, so the app works without the folder, and a file with the same code (`en.json`, `uk.json`) replaces the built-in text.

To add a language, copy `en.json` to a new file named after the language code (e.g. `pl.json`) and translate it:

```json
{
  "name": "Polski",
  "culture": "pl-PL",
  "strings": {
    "AddClock": "Dodaj zegar",
    "BlurAmount": "Rozmycie: {0}%",
    ...
  }
}
```

- `name`: how the language appears in the menu.
- `culture`: used for the day and month names on the clock and in the calendar, and for city names in the weather.
- `{0}`: where the app puts a number or name; keep it in the translation.
- Strings you leave out are shown in English; a file that isn't valid JSON is skipped.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for what changed in each version.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows.

```powershell
git clone https://github.com/YevhenVe/win_widgets_oneui.git
cd win_widgets_oneui\src\DesktopWidgets
dotnet run
```

Pass `--preview` (`dotnet run -- --preview`) to show the widgets above all windows while developing.

Publish a self-contained single-file build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o ..\..\dist\standalone
```

Don't add `EnableCompressionInSingleFile`: the compressed assemblies get unpacked into process memory and nearly double the private footprint. Zip the output instead.

## How it works

- **C# / .NET 10 / WPF**, no third-party packages; Win32 and shell APIs are called via P/Invoke.
- **Blur**: Windows 11's own backdrops don't work here (acrylic turns opaque on inactive windows, the legacy accent blur renders black). Since widgets sit directly on the desktop, the app instead reads every monitor's wallpaper through `IDesktopWallpaper`, composes and blurs it once, and paints the matching slice behind each widget (`Backdrop.cs`). A `FileSystemWatcher` on Windows' transcoded wallpaper copy detects wallpaper changes.
- **Desktop layer**: `WM_WINDOWPOSCHANGING` is intercepted to keep the windows just above `Progman` (`WidgetWindow.cs`).
- **Small footprint**: software rendering (no Direct3D device), conservative GC, and the working set is trimmed after bursts of work (`MemoryTrim.cs`). The tray icon uses `Shell_NotifyIcon` directly instead of WinForms.

| File | Purpose |
|---|---|
| `WidgetWindow.cs` | Base pill window: frost, drag, scale, z-order, context menu, tint |
| `ClockWidget.cs` | Analog clock and date |
| `WeatherWidget.cs`, `WeatherService.cs`, `WeatherIcons.cs` | Weather data, location and icons |
| `Backdrop.cs` | Wallpaper composition and blur |
| `TrayIcon.cs` | Notification-area icon and menu |
| `Settings.cs` | JSON settings |

## Credits

Weather data by [Open-Meteo](https://open-meteo.com/) (CC BY 4.0). IP geolocation by [ipapi.co](https://ipapi.co/) with [ip-api.com](https://ip-api.com/) as a fallback. Design inspired by Samsung One UI; this project isn't affiliated with Samsung.
