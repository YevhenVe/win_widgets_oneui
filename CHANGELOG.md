# Changelog

## 0.7.1 — 2026-11-12

### Fixed
- Start with Windows works reliably. It now uses a shortcut in the Startup folder, because Windows sometimes skipped the old registry entry at sign-in. Existing setups switch over automatically on first start.
- After moving or updating the app, Start with Windows points to the copy you run.

## 0.7.0 — 2026-11-07

### New
- Clicking the weather widget opens the Windows Weather app (weather.com if the app is not installed).
- The calendar opens centred under the clock; it only shifts when it would leave the screen.

### Fixed
- Widgets stay visible on Win+D (Show desktop) and go back under your windows when you leave it.
- Clicking the clock or opening a widget menu no longer lifts the widgets above other windows.
- The calendar no longer closes right after opening.
- After an Explorer restart the widgets reattach to the desktop and reopen if Windows closed them.

## 0.6.1 — 2026-11-04

### Fixed
- Widgets no longer close on Alt+F4 after the last open window is closed. Only Remove widget and Exit close them.

## 0.6.0 — 2026-11-04

### New
- The clock shows the full day and month names, capitalized, in English and Ukrainian.
- Text size slider in the clock's right-click menu; text that doesn't fit ends with an ellipsis.
- Short format option in the same menu (Wed, 4 Nov).

## 0.5.0 — 2026-11-04

### New
- Click the clock to open a month calendar in the widget style.
- Tint slider instead of themes: black 50 % – transparent – white 50 %. Widgets are transparent by default.
- Blur amount 0 turns the blur off completely; the separate blur buttons are gone.
- Both sliders move in 1 % steps, and the mouse wheel changes them by one.
- Auto text colour follows the actual background under the widget.
- Menus and the location window follow the Windows light or dark mode.

## 0.4.0 — 2026-10-29

### New
- Dark theme with a translucent dark plate under the widgets.
- Modern rounded menus for the tray and the widgets.
- Text colour can be set manually (dark or white).

### Fixed
- Menu colours after switching the theme.

## 0.3.0 — 2026-10-29

### New
- Language selection: English and Ukrainian, switched live from the tray.
- City names are shown in Ukrainian where a translation exists.

## 0.2.1 — 2026-10-27

### Fixed
- The frosted background was shifted on portrait and square wallpapers.

## 0.2.0 — 2026-10-27

### New
- Blur amount slider.
- Soft widget shadow that can be turned off.
- New wallpapers are picked up in about a second; Refresh background in the tray menu.

## 0.1.0 — 2026-10-26

- First release: One UI-style analog clock and current weather widgets with a frosted wallpaper background, free placement and scaling, light and dark text, tray menu and start with Windows.
