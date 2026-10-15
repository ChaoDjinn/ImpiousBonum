<p align="center">
  <img src="docs/images/icon.png" width="96" height="96" alt="Impious Bonum icon">
</p>

<h1 align="center">Impious Bonum</h1>

<p align="center">
  <em>Dog-Latin for "wicked good".</em><br>
  A lightweight system dashboard for a spare screen, like the little touchscreen strip under your main monitor.<br>
  A native replacement for Rainmeter + HWiNFO setups, with no other monitoring software to run alongside it.
</p>

<p align="center">
  <a href="https://github.com/ChaoDjinn/ImpiousBonum/releases/latest"><img src="https://img.shields.io/github/v/release/ChaoDjinn/ImpiousBonum?color=FF9800" alt="Latest release"></a>
  <a href="https://github.com/ChaoDjinn/ImpiousBonum/actions/workflows/ci.yml"><img src="https://github.com/ChaoDjinn/ImpiousBonum/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/ChaoDjinn/ImpiousBonum" alt="MIT license"></a>
</p>

<p align="center"><img src="docs/images/dashboard.png" alt="The default dashboard on a 1920×480 screen: CPU, GPU and memory graphs, CPU and GPU temperatures, 145 FPS, the time, three drives, and download, upload and ping"></p>

<p align="center">
  <a href="https://github.com/ChaoDjinn/ImpiousBonum/releases/latest/download/ImpiousBonum-win-Setup.exe"><b>Download the installer</b></a>
  ·
  <a href="docs/guide.md">User guide</a>
  ·
  <a href="docs/widgets.md">Widget reference</a>
  ·
  <a href="gallery">Layout gallery</a>
</p>

## What it is

Impious Bonum turns a spare screen into a live view of your PC: CPU, GPU, memory, temperatures, FPS, drives and
network, alongside a clock, the weather, your calendar, what's playing and buttons to control it all. Lay it out how
you like in a visual editor, pick from 43 themes, and it runs quietly in the tray using about 50 MB of memory. No
spare screen? It works as a window too, or on a tablet over Wi-Fi.

## Features

**Your PC at a glance**
- CPU, memory, GPU, drives and network out of the box, down to each network adapter's speeds, the Wi-Fi signal and a laptop's battery time left; add the optional sensor service for temperatures, fans, clocks, power and per-app FPS with frame times and 1% lows, about 280 metrics on a typical PC. [More](<docs/guide.md#what-it-shows>)
- Graphs, multi-line graphs, gauges, stat tiles, every CPU thread as a heatmap, drives, and the apps using the most CPU or memory. [More](<docs/guide.md#widgets-in-depth>)
- Session recaps when you finish playing a game you've linked to a layout: average, lowest and highest FPS, loads and temperatures. [More](<docs/guide.md#session-recaps>)
- Alerts when a reading stays out of bounds: a notification, a banner on the dashboard, a sound, a switch to another layout, a script to run, or a message to Home Assistant or your phone (via ntfy). [More](<docs/guide.md#alerts>)
- Logging to CSV files for a spreadsheet. [More](<docs/guide.md#logging>)

<table>
  <tr>
    <td width="50%"><img src="docs/images/widgets/frame-times.png" alt="Stat tiles of the frame rate at 145 and the 1% and 0.1% lows, each with a trend line and its range, and a graph of the average and slowest frame times"><br><b>Frame rate</b>: FPS, 1% and 0.1% lows, and frame times</td>
    <td width="50%"><img src="docs/images/widgets/gauges.png" alt="Three gauges: a 270° dial of the CPU temperature at 65, a half dial of GPU load at 90.4 % and a ring of memory use at 55 %"><br><b>Gauges</b>: a dial, a half dial or a ring</td>
  </tr>
</table>

**Your day**
- Clocks with other time zones, timers and countdowns, your calendar's agenda (or the next event in just one calendar), and news headlines as a list or a ticker for each feed. [More](<docs/guide.md#world-clocks>)
- The weather, with no account needed: up to 16 days or 48 hours ahead, the details you choose, more places to move between, air quality and pollen, and severe weather warnings in the UK and US. [More](<docs/guide.md#weather>)
- What's playing, with album art and controls, an audio visualiser, and buttons that open programs, send shortcuts or switch layouts. [More](<docs/guide.md#now-playing-and-buttons>)
- Your own data from any web address or command. [More](<docs/guide.md#custom-data>)

<table>
  <tr>
    <td width="50%"><img src="docs/images/widgets/weather.png" alt="The weather widget with colour icons: 14° and partly cloudy now with the high, low and feels-like, four days ahead and a yellow wind warning under them, and every three hours ahead with the chance of rain"><br><b>Weather</b>: now, the days ahead and the hours ahead</td>
    <td width="50%"><img src="docs/images/widgets/agenda.png" alt="An agenda of today's and tomorrow's events with their times and places, and the next event, Call with Sam at 22:15"><br><b>Calendar</b>: today, tomorrow and what's next</td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/widgets/media.png" alt="Now playing: album art, the title and artist, a progress bar and previous, pause and next buttons, above four buttons: Mute mic, Screenshot, Gaming and Sound"><br><b>Now playing</b>, and buttons for shortcuts, apps and layouts</td>
    <td width="50%"><img src="docs/images/widgets/clocks.png" alt="A clock at 21:47 with the date, and the time in San Francisco, New York, Tokyo and Sydney"><br><b>Clock</b> with the time in other places</td>
  </tr>
</table>

**Make it yours**
- A layout editor with live preview, drag and resize, a searchable picker for every metric, fonts you can see before you pick, and text that shrinks to fit, plus touch editing on the dashboard itself. [More](<docs/guide.md#layout-editor>)
- 43 built-in themes, from clean to retro to neon, with gradients, glow, and picture, GIF or video backgrounds. [More](<docs/guide.md#layout-editor>)
- A gallery of ready-made layouts to try with your own readings and import, and export to share yours. [More](<docs/guide.md#layout-gallery>)

**It fits around you**
- Different layouts for different games, a schedule for times of day or dark mode, pages to swipe between, and global hotkeys. [More](<docs/guide.md#automatic-behaviour>)
- Dims or blacks out when you're away. [More](<docs/guide.md#dimming>)
- On a spare monitor (or several, each with its own layout), as a window, or on a tablet or phone in its browser. Support for small 3.5" USB screens is there too, but hasn't been tried on a real one yet. [More](<docs/guide.md#more-screens>)
- Light: about 50 MB of memory and a fraction of a percent of the CPU. A per-user installer with no admin prompt, and small automatic updates.

## A look for every desk

43 built-in themes, or make your own. Here's the default layout in eight of them:

<p align="center"><img src="docs/images/themes.png" alt="The default layout in the Outrun, Nord, Dracula, Solarized Light, Amber, Cyberpunk, Blueprint and Catppuccin themes"></p>

Or start from a layout in the [gallery](gallery):

<table>
  <tr>
    <td width="50%"><img src="gallery/images/gaming-hud.png" alt="Gaming HUD: a big frame rate, frame times, 1% lows, CPU and GPU load, temperature gauges and the session so far"><br><b>Gaming HUD</b>: frame rate, frame times and the session so far</td>
    <td width="50%"><img src="gallery/images/system-monitor.png" alt="System Monitor: CPU, GPU and network lines, every CPU thread as a heatmap, the busiest apps, drives and memory"><br><b>System Monitor</b>: loads, every CPU thread, the busiest apps</td>
  </tr>
  <tr>
    <td width="50%"><img src="gallery/images/desk-companion.png" alt="Desk Companion: the time here and in two other places, the day's calendar, the weather and what's playing"><br><b>Desk Companion</b>: world clocks, calendar, weather and music</td>
    <td width="50%"><img src="gallery/images/focus.png" alt="Focus: a 25-minute focus timer, the next event, the time and what's playing"><br><b>Focus</b>: a focus timer, your next event and your music</td>
  </tr>
</table>

Lay out your own in the editor, with a live preview, a searchable list of every reading and the same changes on the dashboard as you make them:

<p align="center"><img src="docs/images/editor.png" alt="The Impious Bonum window on its Layouts page: the sidebar of pages, the screen and layout pickers, the widget list, the layout's preview with the GPU graph selected, and the graph's settings"></p>

<sub>Every picture here is drawn by the app itself from made-up readings (`build/screenshots.ps1`), so it shows what the app really draws without showing anyone's PC. The [user guide](<docs/guide.md>) has more.</sub>

## Get started

1. Download and run [`ImpiousBonum-win-Setup.exe`](https://github.com/ChaoDjinn/ImpiousBonum/releases/latest/download/ImpiousBonum-win-Setup.exe) (Windows 10 or 11). It installs for your user only and adds the .NET runtime if needed. SmartScreen may warn the first time, since the installer isn't code-signed yet: choose *More info → Run anyway*.
2. The dashboard opens on your smallest secondary monitor, or as a window on your main screen if that's the only one. Double-click the tray icon (or start Impious Bonum again) for its window, where *Screens* moves it or makes it a window and *General* has *Start with Windows*.
3. For temperatures and FPS, *Sensors* → *Install…* in the window (one admin prompt). CPU temperatures also need the free [PawnIO](https://pawnio.eu) driver (`winget install namazso.PawnIO`; HWiNFO and FanControl install it too). GPU temperatures and FPS work without it.
4. *Layouts* is the layout editor, and *Gallery* has ready-made layouts for screens from 3.5" to 4K, filtered to suit yours.

The [user guide](<docs/guide.md>) covers everything else, and the [widget reference](docs/widgets.md) lists every widget's settings.

## Documentation

- [User guide](<docs/guide.md>): installing, the editor, every feature and how to set it up
- [Widget reference](docs/widgets.md): every widget and theme setting, its default and its range
- [Layout gallery](gallery): ready-made layouts, and how to add yours
- [Changelog](CHANGELOG.md): what's new in each release
- [Developing Impious Bonum](docs/development.md): building from source, releases, update signing and the code's layout
- [Contributing](CONTRIBUTING.md) and [security](SECURITY.md)

## Help and ideas

Something not working, or an idea for a widget or a feature? [Open an issue](https://github.com/ChaoDjinn/ImpiousBonum/issues/new/choose),
or from the app: *About and updates* → *Report a problem*. Please report security problems privately, as
[SECURITY.md](SECURITY.md) explains.

## Good to know

Impious Bonum is young: it's at version 0.15, first released at the end of September 2026, and it changes quickly
(installed copies update themselves). Some things it doesn't do yet, or does with a catch:

- **Windows 10 and 11 only.**
- **The installer isn't code-signed**, so SmartScreen warns the first time. Updates are signed with the project's own
  key, and installed copies only take a release with a matching signature ([how](docs/development.md#update-signing)).
- **USB screens haven't been tried on a real screen yet**, and only the 3.5" "revision A" kind is supported.
- **CPU temperatures need the PawnIO driver** as well as the sensor service (above).
- **The tablet view is plain HTTP** on your own network, updated once a second, and shows a still frame of moving
  backgrounds. Anyone with its link can see the dashboard ([details](<docs/guide.md#tablet-view>)).
- **Claude usage needs Claude Code in a terminal** on a Pro or Max plan; the Claude Desktop app doesn't report it.
- **Calendars are read from iCal links** (Google, Outlook and iCloud all have one), not by signing in.
- **Severe weather warnings are for the UK and US only** (the Met Office and the National Weather Service), and
  **pollen is only counted in Europe**, in season.

## Roadmap

- [ ] Code signing
- [ ] Tried and tested on real USB screens, and support for the 5" and 8.8" models
- Ideas are welcome as [issues](https://github.com/ChaoDjinn/ImpiousBonum/issues/new/choose).

<details>
<summary>Done so far</summary>

- [x] Borderless dashboard on a chosen monitor, remembered placement, the default layout
- [x] Sensor host: elevated service (LibreHardwareMonitorLib) for temperatures, fans, clocks and power, over a named pipe
- [x] Layout editor: live preview, drag/resize, generated properties, theme, undo/redo
- [x] Metric picker: browse and search every metric with live values, insert into templates
- [x] Touch edit mode on the dashboard itself, synced with the editor
- [x] Installer and automatic updates (Velopack, GitHub Releases), with signed updates
- [x] Vulkan/OpenGL frame counting
- [x] Tablet view: the dashboard in a browser over the local network
- [x] Bars, and Claude plan usage from Claude Code
- [x] More widget types: 22 so far
- [x] Themes: 43 built in, with gradients, glow and moving backgrounds
- [x] Screenshots of the real dashboard, the editor and the widgets, here and in the guide
- [x] A window with a page for every setting, and a gallery of 13 ready-made layouts for screens from 3.5" to 4K
- [x] Weather: longer forecasts, more readings, more places, air quality and pollen, and severe weather warnings
- [x] Alerts that play a sound, switch layout, run a program or send a web request
- [x] Network adapters, Wi-Fi signal and battery time left

</details>

## License

[MIT](LICENSE)
