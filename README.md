<p align="center">
  <img src="docs/images/icon.png" width="96" height="96" alt="Impious Bonum icon">
</p>

<h1 align="center">Impious Bonum</h1>

<p align="center">
  <em>Dog-Latin for "wicked good".</em><br>
  A lightweight system dashboard for a spare screen, like the little touchscreen strip under your main monitor.<br>
  A native replacement for Rainmeter + HWiNFO setups, with no third-party monitoring software required.
</p>

<p align="center">
  <a href="https://github.com/ChaoDjinn/ImpiousBonum/releases/latest"><img src="https://img.shields.io/github/v/release/ChaoDjinn/ImpiousBonum?color=FF9800" alt="Latest release"></a>
  <a href="https://github.com/ChaoDjinn/ImpiousBonum/actions/workflows/ci.yml"><img src="https://github.com/ChaoDjinn/ImpiousBonum/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/ChaoDjinn/ImpiousBonum" alt="MIT license"></a>
</p>

<!-- Screenshot of the real dashboard. Render one with: ImpiousBonum.exe --snapshot docs\images\dashboard.png
     then remove these comment markers:
<p align="center"><img src="docs/images/dashboard.png" alt="The dashboard on a 1920×480 screen"></p>
-->

<p align="center">
  <a href="https://github.com/ChaoDjinn/ImpiousBonum/releases/latest/download/ImpiousBonum-win-Setup.exe"><b>Download the installer</b></a>
  ·
  <a href="#layout-editor">Layout editor</a>
  ·
  <a href="docs/widgets.md">Widget reference</a>
</p>

## Highlights

- **Made for a spare screen, or a window.** Pick a monitor; the dashboard fills it and comes back to the same place after reboots or display changes. No spare screen? Make it a window you can drag, resize, lock in place and keep on top. It's hidden from the taskbar and Alt+Tab, and tapping it never steals focus.
- **The numbers you'd run HWiNFO for, built in.** CPU, memory, GPU, drives and network out of the box. The optional sensor service adds temperatures, fans, clocks, power and per-app FPS, about 280 metrics on a typical PC.
- **Edit it visually, or by hand.** A layout editor with live preview, drag and resize, a searchable metric picker and touch editing on the dashboard itself. Underneath it's one JSON file that applies live as you save it.
- **Tiny footprint.** About 50 MB of private memory and well under 0.1% CPU when idle; the sensor service adds about 20 MB.
- **Installs and updates itself.** A per-user installer with no admin prompt, and small background updates from GitHub Releases.

## The default layout

Out of the box it fills a 1920×480 screen like this (any size works: the canvas scales to fit, and you can change the design size):

<p align="center"><img src="docs/images/default-layout.svg" alt="Map of the default layout: CPU and memory graphs, GPU load and VRAM, CPU and GPU temperatures, FPS, a clock, drives, and network download, upload and ping"></p>

Every box is a widget you can move, resize, restyle or replace. Six widget types are available: `text`, `graph` (line or area history with a header), `icon` (accent icon + value), `clock`, `drives` and `rows`. Every setting, its default and its range is in the [widget reference](docs/widgets.md).

## What it shows

| Metric ids | Source |
|---|---|
| `cpu.load`, `cpu.threads` | `GetSystemTimes` |
| `mem.used`, `mem.total`, `mem.available`, `mem.load` | `GlobalMemoryStatusEx` |
| `gpu.name`, `gpu.load`, `gpu.vram.used`, `gpu.vram.total` | GPU performance counters + DXGI (same numbers as Task Manager) |
| `disk.<L>.free/used/total/usedPct/label` | `DriveInfo`, follows drives as they come and go |
| `net.down`, `net.up` | Adapters with a default gateway |
| `net.ping` | ICMP to `1.1.1.1` (configurable) |
| `cpu.temp`, `cpu.power`, `gpu.temp`, `gpu.hotspot`, `gpu.power`, `gpu.fan` | Sensor service (below) |
| `fps`, `fps.app` | Sensor service counts frames per app (like PresentMon/HWiNFO): DirectX, Vulkan and OpenGL, without hooking into the game; the tray's *FPS from* picks the foreground app or the top app on a chosen monitor |
| `hw/...` (every sensor LibreHardwareMonitor finds: clocks, voltages, fans, per-core loads, …) | Sensor service. Run `ImpiousBonum.Sensors.exe list` to see the ids on your machine |
| `sensors.status` | Text describing the sensor service connection |

## Installing

Download [`ImpiousBonum-win-Setup.exe`](https://github.com/ChaoDjinn/ImpiousBonum/releases/latest/download/ImpiousBonum-win-Setup.exe) from the [latest release](https://github.com/ChaoDjinn/ImpiousBonum/releases/latest) and run it.

- It installs for your user only (no admin prompt) into `%LocalAppData%\ImpiousBonum`, adds Start menu and desktop shortcuts, and installs the .NET 10 Desktop Runtime first if you don't have it.
- On first run it picks your smallest secondary monitor. Use the tray icon to move it to another display, edit the layout, or turn on *Start with Windows*.
- The app checks GitHub for updates about a minute after it starts and every six hours after that; tray → *Check for updates* checks right away. New versions download in the background (usually as a small delta) and the tray offers *Restart to update to x.y.z*. The tray menu's first line shows the installed version.
- After an update, if the sensor service is from an older version the tray says so; *Sensors → Update sensor service…* brings it up to date.
- Uninstall from Windows Settings → Apps. It removes start-with-Windows and offers to remove the sensor service (one UAC prompt). Your layout and settings in `%AppData%\ImpiousBonum` are kept.
- The installer isn't code-signed yet, so Windows SmartScreen may warn the first time: choose *More info → Run anyway*.

Settings and layouts live in `%AppData%\ImpiousBonum`:

- `layouts\<name>.json`: your saved layouts (canvas size, theme and widgets), one file each. The dashboard shows one at a time; saved changes to it apply immediately. Upgrading from a version with a single `layout.json` moves it to `layouts\Default.json`, hand edits and all.
- `themes\<name>.json`: your saved themes (font, colours and background), which any layout can use by name.
- `settings.json`: which monitor, how the window behaves (`fill`, `area`, `locked`, `clickThrough`, `alwaysOnTop`, `hideOverFullscreen`), which layout (`activeLayout`), ping host, `hardwareRendering` (off by default to save memory).
- `dashboard.log`: startup, update checks and any errors. If something goes wrong, this is the file to attach to an issue (tray → *Open settings folder*).

### Tray menu

| Item | |
|---|---|
| *Display* | Which monitor the dashboard is on, and how its window behaves (see [Windowed mode](#windowed-mode)) |
| *Layout* | Which saved layout the dashboard shows. Switches straight away and is remembered |
| *Layout → Import…* | Add a layout from an exported `.ibl` file and show it (see [Sharing layouts](#sharing-layouts)) |
| *Layout → Game layouts* | Link the app you were just using to a layout, remove links, and turn automatic switching on or off (see [Game layouts](#game-layouts)) |
| *Sensors* | Sensor service status, and install, update or remove it |
| *FPS from* | Follow the foreground app, or the top app on a chosen monitor |
| *Edit layout…* | Open the layout editor (or double-click the tray icon) |
| *Open layout file*, *Open settings folder*, *Reload layout* | Work with the files directly (*Open layout file* opens the one being shown) |
| *Start with Windows* | Start the dashboard when you sign in |
| *Check for updates* | Installed copies only |

### Windowed mode

Without a spare screen, tray → *Display* → *Windowed* turns the dashboard into a window on the chosen monitor. It starts unlocked with a dashed outline: drag it to move it (onto another monitor too) and drag an edge or corner to resize it. It keeps the layout's shape, so the layout always fills it. Then tick *Lock position*. Its size, position and monitor are remembered, and it's moved back on screen if the resolution changes.

- *Click-through when locked* lets clicks go to the window underneath. Right-click can't open the editor then; use the tray.
- *Always on top* keeps it above other windows. *Hide over fullscreen apps* (on by default) hides it while a game or video fills its monitor, since a window over a borderless game can cost frame pacing, and it can't show over exclusive fullscreen anyway.
- *Bring to front* raises it when it's behind other windows, and *Reset size and position* recentres it.
- The canvas scales to the window, so a small window shrinks everything evenly. For a window that isn't a strip, give the layout a matching size in the editor (*Canvas*); *Match dashboard screen* uses the window's size.

*Fill screen* goes back to covering the whole monitor.

## Sensor service (temperatures, fans, power, FPS)

Reading CPU temperatures needs admin rights and a kernel driver, so it lives in a separate process: `sensors\ImpiousBonum.Sensors.exe`, built on [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor). The dashboard itself stays unelevated and receives readings over a read-only named pipe.

```mermaid
flowchart LR
    lhm["LibreHardwareMonitor<br/>+ PawnIO driver"] --> sensors
    etw["Frame events: DirectX,<br/>Vulkan, OpenGL (ETW)"] --> sensors
    subgraph svc["Windows service (elevated)"]
        sensors["Sensor service<br/>ImpiousBonum.Sensors.exe"]
    end
    subgraph you["Your session (no admin)"]
        dash["Dashboard<br/>ImpiousBonum.exe"]
    end
    win["Windows APIs<br/>CPU, memory, GPU,<br/>drives, network"] --> dash
    sensors -- "read-only<br/>named pipe" --> dash
```

- **Install:** tray icon → *Sensors* → *Install sensor service…* (one UAC prompt). The host is copied to `Program Files\Impious Bonum\Sensors` and registered as an auto-start service, so there are no prompts at logon.
- **CPU temperatures** also need the signed [PawnIO](https://pawnio.eu) driver (`winget install namazso.PawnIO`). HWiNFO and FanControl install it too. GPU sensors work without it.
- The service only reads hardware while a dashboard is connected. Its log is in `%ProgramData%\ImpiousBonum\sensors.log`.
- For development: `ImpiousBonum.Sensors.exe run` serves from a console, and `list` prints every sensor. Both work unelevated with fewer sensors. `install` and `uninstall` (as admin) do what the tray items do.

## Layout editor

Double-click the tray icon (or tray → *Edit layout…*, or right-click / press and hold on the dashboard) to open the editor on your main screen:

- **Preview:** click a widget to select it, drag to move, drag the handles to resize. Moves snap to a grid (toolbar); hold Alt to place freely. Arrow keys nudge, Shift+arrows by 10.
- **Metrics:** template and metric fields have a `{…}` button that opens a searchable list of every metric (about 280 on a typical PC, including every raw sensor) with live values. Pick one and choose how to show it (e.g. `18.7 GB`, `19,138 MB` or just `18.7`) and it's inserted at the cursor. Under each field a live preview shows what it renders now and flags unknown metric ids.
- **Properties:** every setting of the selected widget, with the right control for each (numbers, colours with a picker, fonts, drop-downs, lists) and a reset-to-default button. Click empty canvas or press Esc for canvas size and theme.
- **Widgets:** add, duplicate (Ctrl+D), delete, and bring forward / send back.
- **On the dashboard itself:** while the editor is open, the dashboard shows outlines and finger-sized handles. Tap a widget to select it, drag to move, drag a handle to resize; the editor follows along (and vice versa), with the same snapping and undo.
- **Themes:** with the canvas selected, *Theme* picks a saved theme for the layout: *Orange* (the default look), *Ice* and *Terminal* are built in. Colours and fonts you change below it apply to this layout only (↺ goes back to the theme's value). *Save theme as…* keeps the current look as a new theme for other layouts, *Update theme* writes your changes into a theme of your own (restyling every layout that uses it), and *Detach* copies the theme's values into the layout so later changes to the theme don't affect it. Built-in themes can't be changed; *Save theme as…* makes your own copy.
- **Saved layouts:** the editor edits the layout the dashboard is showing. The *Layout* drop-down switches to another (asking first if you have unsaved changes), and *Layouts ▾* has *Save as…* (your changes as a new layout), *Rename…*, *Duplicate…*, *Export…*, *Import…* and *Delete* (not the last one). Keep a "Gaming" and a "Desktop" layout, for example, and flip between them from the tray.
- The real dashboard shows your changes live while you edit. **Save** (Ctrl+S) writes the layout's file; closing without saving puts the dashboard back as it was. Undo/redo with Ctrl+Z / Ctrl+Y.

<!-- Editor screenshot (Win+Shift+S): save as docs/images/editor.png, then remove these comment markers:
<p align="center"><img src="docs/images/editor.png" alt="The layout editor with the metric picker open"></p>
-->

## Sharing layouts

*Layouts ▾ → Export…* in the editor saves the layout you're editing as one `.ibl` file, with its font file and background image inside, so it works on another PC. A layout using one of your saved themes takes the theme's values with it; built-in themes stay as names. *Import…* (in the editor, or tray → *Layout*) adds it to your saved layouts, asking before replacing one with the same name. The font and image go in `layouts\<name>\` and are removed when no layout uses them any more. Metrics the other PC doesn't have, such as a particular sensor, show `—` as usual.

An `.ibl` file is a zip of `layout.json`, `manifest.json` and an `assets` folder. Imports only take the layout and the font (`.ttf`, `.otf`) and image it names, reject a file with `..` or absolute paths in it, and cap sizes (1 MB for the layout, 50 MB per file).

## Game layouts

A game can have its own layout: FPS front and centre, its colours, a themed font. When a linked game has been in front for about 3 seconds the dashboard switches to its layout, and when it has been gone for about 3 seconds it goes back to the layout you chose. Alt-tabbing out briefly, or a launcher flashing up, doesn't switch anything.

To link a game: with the game running, open tray → *Layout* → *Game layouts* → *Link "game" to* and pick a layout (the menu offers the last app you had in front, so click the tray icon straight after leaving the game). To keep your usual layout and only change its look, pick a theme under *Theme only* instead. The same menu lists the links with *Remove link*, and *Switch automatically* turns the whole thing off and on (remembered).

"In front" means the same thing as for the FPS reading: the foreground app, or with *FPS from → Top app on a monitor*, the top app on that monitor, so a game on one screen keeps its layout while you type on another.

Game layouts are temporary: picking a layout from the tray while a game is running makes that your choice to come back to, and the editor keeps whichever layout it opened on (switching waits until it closes). Switches are written to `dashboard.log`. A link to a layout or theme that no longer exists is ignored, with one notification and a line in the log; renaming a layout updates its links.

Links live in `settings.json` (tray → *Open settings folder*) and can be edited by hand while the dashboard isn't running. `process` is the program's name as Task Manager's *Details* tab shows it, with or without `.exe`, matched ignoring case. A link has a `layout`, a `theme` (which restyles whichever layout is showing), or both:

```json
"gameLayouts": [
  { "process": "eldenring", "theme": "Elden Ring" },
  { "process": "Cyberpunk2077.exe", "layout": "FPS" },
  { "process": "doom", "layout": "FPS", "theme": "Terminal" }
],
"gameLayoutsEnabled": true
```

## Editing layouts by hand

Each layout is a JSON file in `%AppData%\ImpiousBonum\layouts` (tray → *Open layout file* opens the one being shown). Saving the shown layout's file applies it straight away; changes to other layouts show when you switch to them. To add a layout by hand, drop a new `.json` file in the folder and pick it from tray → *Layout*.

The canvas has a design size (default 1920×480) and scales to fit the window. Each widget has `type`, `x`, `y`, `width`, `height` plus its own settings. Text uses templates:

```json
{ "type": "text", "x": 58, "y": 296, "width": 427, "height": 76, "fontSize": 64, "text": "{gpu.vram.used:N0 MB}" }
```

`{id}` formats automatically (`18.7 GB`, `11.8 %`, `79.7 KB/s`). After a colon you can add a number format (`0.0`, `N0`), force a byte unit (`MB`, `GB`, …) or write `nounit`. Readings that aren't available show as `—`.

To make a value change colour when it runs hot, give its `text`, `icon`, `graph` or `drives` widget warning colours (in the editor, or as `thresholds` in the layout file): `[{ "above": 80, "color": "warning" }, { "above": 90, "color": "critical" }]`. `warning` and `critical` are theme colours, and `below` works too, e.g. for a low frame rate.

If a layout has a typo or an out-of-range value, the tray shows a notification naming the file and saying what and where, e.g. *unknown setting 'fontsize' (did you mean 'fontSize'?)*.

A layout's `theme` can be its own values, a saved theme's name (`"theme": "Ice"`), or a saved theme with a few values changed for this layout: `"theme": { "base": "Ice", "accent": "#FF4081" }`. Saved themes are files in `%AppData%\ImpiousBonum\themes` holding the same settings; saving one restyles every layout that uses it straight away. An unknown theme name is reported like any other typo, and the layout falls back to the default look.

To use a font you don't want to install, point the theme at the file: `"fontFile": "C:\\Fonts\\SomeFont-Light.otf"`.

To put a picture behind the widgets, add `"backgroundImage": "C:\\Pictures\\wallpaper.jpg"` to the theme. `backgroundFit` is `fill` (the default: covers the canvas, cropping the edges), `fit`, `stretch`, `center` or `tile`, and `backgroundOpacity` below 1 lets the `background` colour show through, which dims a busy picture on black. The image is decoded at the canvas size, so a 4K wallpaper doesn't cost extra memory. If the file is missing the dashboard just shows the colour. The full list of theme settings is at the end of [docs/widgets.md](docs/widgets.md#theme).

## Command line

| Option | |
|---|---|
| `--data-dir <dir>` | Use a different settings folder |
| `--snapshot <file.png>` | Render the layout with live data to a PNG and exit |
| `--layout <name>` | With `--snapshot`, render this saved layout instead of the one being shown |
| `--warmup <seconds>` | Sampling time before a snapshot (default 3) |
| `--widget-docs <file>` | Regenerate the widget reference from the widget descriptors |

## Development

### Running from source

Requires the .NET 10 SDK on Windows 10/11.

```bash
dotnet run --project src/ImpiousBonum.App -c Release
```

Tests: `dotnet test ImpiousBonum.slnx -c Release` (CI runs the same on every push to `main` and every pull request).

A build run from source shows *development build* in the tray and never checks for updates; only installed copies do.

`docs/widgets.md` is generated from the widget descriptors, and a test fails if it's out of date. After changing a widget's settings, regenerate it:

```bash
dotnet run --project src/ImpiousBonum.App -c Release -- --widget-docs docs/widgets.md
```

### Making a release

1. Set `<Version>` in `Directory.Build.props` to the new version and merge that to `main`.
2. Tag that commit on `main` with the same version and push the tag:

   ```bash
   git tag v0.2.1
   ```

   ```bash
   git push origin v0.2.1
   ```

The [release workflow](.github/workflows/release.yml) then runs the tests, downloads the previous release so it can build a delta package, packs the installer with Velopack and publishes the GitHub release *Impious Bonum 0.2.1*. Installed copies pick it up on their next update check. The tag decides the version that ships, so keep it in step with `Directory.Build.props`; a version number can only be released once.

To build the installer locally instead: `pwsh build/publish.ps1 -Version 0.2.1` (output in `artifacts/releases`; add `-SkipTests` to skip the test run).

### Update signing

Installed copies only take a release whose full package is signed with the project's update key. The public half is `assets/update-signing-key.pub`, built into the app; the private half is the `UPDATE_SIGNING_KEY` secret of the `release` environment, and the release workflow uploads `ImpiousBonum-<version>-signature.txt` with each release. This is separate from Authenticode code signing, which the installer doesn't have yet.

One-time setup:

1. Create the key (on your own machine, not in CI):

   ```bash
   dotnet run --project tools/ReleaseSigning -- new-key assets/update-signing-key.pub update-signing-key.private.txt
   ```

2. In the repository settings, under Environments, create `release`, add yourself as a required reviewer, and add the contents of `update-signing-key.private.txt` as the secret `UPDATE_SIGNING_KEY`.
3. Keep a copy of the private key offline, then delete the file. If the key is lost, installed copies can't take updates until they are reinstalled by hand.
4. Commit `assets/update-signing-key.pub`.

Releases then pause for your approval, and fail rather than publish if the secret is missing or doesn't match the public key.

### Layout of the code

```
src/ImpiousBonum.Core    metric store, formatting/templates, providers, sampler (no UI)
src/ImpiousBonum.App     WPF dashboard, widgets, editor, tray, monitor placement, updates
src/ImpiousBonum.Sensors elevated sensor service (LibreHardwareMonitor, ETW frame counting) serving the named pipe
tests/                   unit tests for the core and the app (widgets, layout validation, editor)
docs/widgets.md          widget reference, generated from the widget descriptors
build/                   installer build (publish.ps1) and icon generator
tools/ReleaseSigning     creates the update signing key and signs releases
.github/workflows/       CI (ci.yml) and tag-triggered releases (release.yml)
```

## Roadmap

- [x] Borderless dashboard on a chosen monitor, remembered placement, the default layout
- [x] Sensor host: elevated service (LibreHardwareMonitorLib) for temperatures, fans, clocks and power, over a named pipe
- [x] Layout editor: live preview, drag/resize, generated properties, theme, undo/redo
- [x] Metric picker: browse and search every metric with live values, insert into templates
- [x] Touch edit mode on the dashboard itself, synced with the editor
- [x] Installer and automatic updates (Velopack, GitHub Releases)
- [ ] Code signing
- [x] Vulkan/OpenGL frame counting
- [ ] More widget types
- [x] Themes

## License

[MIT](LICENSE)
