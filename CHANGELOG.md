# Changelog

What's new in each release. Each version's section becomes its release notes on GitHub, so write for people using
the app; changes since the last release go under *Unreleased*, renamed to the version when it's released. The [user guide](docs/guide.md) has the details.

## Unreleased

**Weather widget**
- *Weather now* can be turned off, leaving just the forecast: one widget of the hours ahead beside another of the days, say.
- Full-colour icons: a yellow sun, white clouds, grey rain clouds with blue rain, white lightning. Outline icons in one colour are still the default.
- Its own text and detail colours, instead of always the theme's.

## 0.9.0

Fifteen new features and seven new widgets, from top apps to a layout gallery.

**New widgets**
- **Top apps**: the apps using the most CPU or memory, like a small Task Manager, with an app's processes counted together.
- **Timer**: a stopwatch, a countdown with a notification when it's done, or the time left until a time or date. Tap to start and pause.
- **Agenda**: what's coming up in your calendars, from their iCal links (Google, Outlook, iCloud). Tray → *Calendar…*.
- **News**: headlines from RSS and Atom feeds, as a list or a ticker; tap one to read it. Tray → *News feeds…*.
- **Multi-line graph**: several metrics on one scale, each in its own colour, with a legend.
- **Audio visualiser**: bars that move with whatever the PC is playing. It only listens while it's on screen.
- **CPU threads**: every thread's load as bars or a heatmap. **Stat tile**: a big value with its trend and range. **Images and shapes**: pictures, panels, circles and dividers.
- The **clock** can show other places' time zones too, as a world clock.

**New features**
- **Layout gallery** (tray → *Layout* → *Gallery…*): browse shared layouts drawn live with your own readings, and import one. It starts with five.
- **Hotkeys** (tray → *Hotkeys…*): show or hide the dashboard, turn pages, open the editor or switch layout from anywhere, even in a game.
- **Session recaps**: when you stop playing a linked game, a summary of its FPS, 1% lows, loads and temperatures, also saved to `sessions.csv`.
- **Logging** (tray → *Logging…*): readings to daily CSV files, with each one's average, lowest and highest.
- **Alerts** (tray → *Alerts…*): a notification or a flashing banner when a reading stays out of bounds.
- **Custom data** (tray → *Custom data…*): your own metrics from web addresses and commands.
- **Pages**: swipe between layouts, or rotate through them.
- **Dimming**: dim or black out on a schedule or when you're away, and tap to peek.
- **USB screens** (tray → *Display* → *USB screen*): show a layout on a small 3.5" Turing-style screen. Not yet tried on real hardware, so please report how it goes.
- **Tablets can press buttons** (opt-in, off by default): taps on the tablet view press the layout's buttons and media controls.

**Themes**
- **Gradients and glow**: the accent and background can fade to a second colour, and widgets can glow. Outrun, Vaporwave, Aurora, Magma, Cyberpunk and Toxic use them.
- **Moving backgrounds**: an animated GIF or a video (MP4, WMV) behind the widgets, paused while the dashboard is hidden.

**Also**
- `settings.json` no longer gets `"layout": null` lines.
- A new [user guide](docs/guide.md) covers every feature.

## 0.8.0

**New**
- **40 new built-in themes**, from clean to retro to neon and the well-known editor palettes, with colour samples in the theme picker.
- **Gauge widget**: a dial (arc, semicircle or ring) for any metric.
- **Now playing and buttons**: what's playing with album art and controls, and buttons for media keys, opening programs, keyboard shortcuts and switching layout.
- **Weather**: conditions and forecast where you are, from Open-Meteo, with no account needed.
- **Frame times and 1% lows**: see stutter, not just average FPS.
- **Schedule**: switch theme or layout by time of day, or with Windows' dark mode.

## 0.7.1

- **Drives widget**: any metric in each drive's row, plus the drive's health, temperature and hours from the sensor service.

## 0.7.0

- **More metrics**: GPU memory used %, GPU load per engine (3D, decode, encode, compute), CPU clock, per-thread load, disk activity per drive, uptime and battery.
- **Claude usage**: the app offers to connect Claude Code when you add the widget, and explains what the Desktop app can't do.

## 0.6.0

- **Claude usage widget**: your Claude plan's session and weekly limits, from Claude Code's status line.

## 0.5.1

- The sensor service now picks up PawnIO even when that starts after the service.

## 0.5.0

- **Tablet view**: show the dashboard in a tablet's or phone's browser over your Wi-Fi.

## 0.4.0

- **Windowed mode**: the dashboard as a window you can drag, resize, lock in place and keep on top.

## 0.3.0

- **Saved layouts**: keep several named layouts and switch between them from the tray.
- **Game layouts**: show a game's linked layout while it's in front.
- **Saved themes**: reuse a font and colour scheme across layouts.
- **Sharing layouts**: export a layout, with its font and pictures, as one `.ibl` file, and import others'.
- **Background image** behind the widgets, and **warning colours** when a value crosses a threshold.
- FPS for Vulkan and OpenGL games too.

## 0.2.3

- Re-release of 0.2.2's signed updates (0.2.2 was tagged on an older commit).

## 0.2.2

- Updates are now signed, and the app only installs signed ones.

## 0.2.1

- A log file (`dashboard.log`) for startup, updates and errors, which also records unexpected errors.

## 0.2.0

The first release with an installer.

- A dashboard for a spare screen: CPU, memory, GPU, drives, network and a clock, filling the monitor you pick.
- **Layout editor** with live preview, drag and resize, a metric picker with live values, and touch editing on the dashboard itself.
- **Sensor service** for temperatures, fans, clocks, power and per-app FPS.
- **Installer and automatic updates** from GitHub Releases.
