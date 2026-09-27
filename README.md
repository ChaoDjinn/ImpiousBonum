# Impious Bonum

*Dog-Latin for "wicked good".* A lightweight system dashboard for a spare screen, like the little touchscreen strip under your main monitor. It's a native replacement for Rainmeter + HWiNFO setups, with no third-party monitoring software required.

- Pick a monitor; the dashboard fills it and comes back to the same place after reboots or display changes.
- Hidden from the taskbar and Alt+Tab, and tapping it never steals focus.
- Layout, fonts and colours live in one JSON file that applies live as you save it.
- Small footprint: ~50 MB private memory and well under 0.1% CPU when idle.

## What it shows today

| Metric ids | Source |
|---|---|
| `cpu.load`, `cpu.threads` | `GetSystemTimes` |
| `mem.used`, `mem.total`, `mem.available`, `mem.load` | `GlobalMemoryStatusEx` |
| `gpu.name`, `gpu.load`, `gpu.vram.used`, `gpu.vram.total` | GPU performance counters + DXGI (same numbers as Task Manager) |
| `disk.<L>.free/used/total/usedPct/label` | `DriveInfo`, follows drives as they come and go |
| `net.down`, `net.up` | Adapters with a default gateway |
| `net.ping` | ICMP to `1.1.1.1` (configurable) |
| `cpu.temp`, `gpu.temp`, `fps` | Placeholders until the sensor host lands (see roadmap) |

## Running

Requires the .NET 10 SDK on Windows 10/11.

```bash
dotnet run --project src/ImpiousBonum.App -c Release
```

On first run it picks your smallest secondary monitor. Use the tray icon to move it to another display, edit the layout, or turn on *Start with Windows*.

Settings and layout live in `%AppData%\ImpiousBonum`:

- `layout.json`: canvas size, theme and widgets. Saved changes apply immediately.
- `settings.json`: which monitor, ping host, `hardwareRendering` (off by default to save memory).

### Layout basics

The canvas has a design size (default 1920×480) and scales to fit the window. Each widget has `type`, `x`, `y`, `width`, `height` plus its own settings. Text uses templates:

```json
{ "type": "text", "x": 58, "y": 296, "width": 427, "height": 76, "fontSize": 64, "text": "{gpu.vram.used:N0 MB}" }
```

`{id}` formats automatically (`18.7 GB`, `11.8 %`, `79.7 KB/s`). After a colon you can add a number format (`0.0`, `N0`), force a byte unit (`MB`, `GB`, …) or write `nounit`. Readings that aren't available show as `—`.

Widget types: `text`, `graph` (line or area history with a header), `icon` (accent icon + value), `clock`, `drives`, `rows`.

To use a font you don't want to install, point the theme at the file: `"fontFile": "C:\\Fonts\\SomeFont-Light.otf"`.

### Command line

| Option | |
|---|---|
| `--data-dir <dir>` | Use a different settings folder |
| `--snapshot <file.png>` | Render the layout with live data to a PNG and exit |
| `--warmup <seconds>` | Sampling time before a snapshot (default 3) |

## Roadmap

1. ~~Borderless dashboard on a chosen monitor, remembered placement, the default layout~~
2. **Sensor host**: a small elevated service (LibreHardwareMonitorLib) for CPU/GPU temperatures, fans and clocks, talking to the unelevated UI over a named pipe
3. Metric picker and widget list driven by the metric registry
4. On-screen edit mode: drag, resize and style widgets (touch friendly)
5. FPS via ETW present events, more widget types, themes, installer (Velopack) and signing

## Layout of the code

```
src/ImpiousBonum.Core    metric store, formatting/templates, providers, sampler (no UI)
src/ImpiousBonum.App     WPF dashboard, widgets, tray, monitor placement
tests/                   unit tests for the core
```

## License

[MIT](LICENSE)
